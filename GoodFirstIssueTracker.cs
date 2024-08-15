using System;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Digdir.BDB.GoodFirstIssuesTracker;

public class GoodFirstIssueTracker
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private static readonly HttpClient GithubClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GoodFirstIssueTracker(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _logger = loggerFactory.CreateLogger<GoodFirstIssueTracker>();
        _configuration = configuration;

        if (GithubClient.DefaultRequestHeaders.Authorization is not null) return;
        GithubClient.DefaultRequestHeaders.Add("Authorization", $"token {_configuration.GetValue<string>("GitHubToken")}");
        GithubClient.DefaultRequestHeaders.Add("User-Agent", "AzureFunction-GoodFirstIssues");
        GithubClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    [Function("GoodFirstIssueTracker")]
    public async Task RunAsync([TimerTrigger("0 */30 * * * *", RunOnStartup = true)] TimerInfo myTimer)
    {
        var repos = _configuration.GetValue<string>("GitHubRepos")?.Split(',');
        if (repos is null)
        {
            _logger.LogError("No repositories found in configuration");
            return;
        }
        var orgAndProjectNumber = _configuration.GetValue<string>("GitHubProject")?.Split('/');
        if (orgAndProjectNumber is null)
        {
            _logger.LogError("No organization and project number found in configuration");
            return;
        }

        var projectId = await GetProjectNodeId(orgAndProjectNumber[0], orgAndProjectNumber[1]);

        _logger.LogInformation("Found {0} repositories in configuration, adding to project https//github.com/org/{org}/projects/{project}",
            repos.Length, orgAndProjectNumber[0], orgAndProjectNumber[1]);

        foreach (var repo in repos)
        {
            var repoParts = repo.Split('/');
            var owner = repoParts[0];
            var repoName = repoParts[1];

            var issues = await GetGoodFirstIssues(owner, repoName);
            _logger.LogInformation($"Found {issues.Count} good first issues in {repo}");

            foreach (var issue in issues)
            {
                await AddIssueToProject(issue.Id, projectId);
                _logger.LogInformation($"Added {issue.Url}");
            }
        }

        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
        }
    }

    private static async Task<List<Issue>> GetGoodFirstIssues(string owner, string repoName)
    {
        var query = $@"
        {{
          repository(owner: ""{owner}"", name: ""{repoName}"") {{
            issues(labels: ""good first issue"", first: 100, states: OPEN) {{
              nodes {{
                id
                title
                url
              }}
            }}
          }}
        }}";

        var response = await ExecuteGraphQLQuery<GitHubData>(query);
        var issues = response?.Data.Repository.Issues.Nodes;

        if (issues is null)
        {
            throw new InvalidOperationException("Failed to fetch issues from GitHub");
        }

        return issues;
    }

    private static async Task<string> GetProjectNodeId(string owner, string projectNumber)
    {
        var query = $@"
        {{
          organization(login: ""{owner}"") {{
            projectV2(number: {projectNumber}) {{
              id
            }}
          }}
        }}";

        var response = await ExecuteGraphQLQuery<GitHubData>(query);
        var projectId = response?.Data.Organization.ProjectV2.Id;

        if (projectId is null)
        {
            throw new InvalidOperationException("Failed to fetch project node id from GitHub");
        }

        return projectId;
    }

    private static async Task AddIssueToProject(string issueId, string projectId)
    {
        var mutation = $@"
        mutation {{
          addProjectV2ItemById(input: {{projectId: ""{projectId}"", contentId: ""{issueId}""}}) {{
            item {{
              id
            }}
          }}
        }}";

        await ExecuteGraphQLQuery<dynamic>(mutation);
    }

    private static async Task<T?> ExecuteGraphQLQuery<T>(string query)
    {
        var response = await GithubClient.PostAsync(
            "https://api.github.com/graphql",
            JsonContent.Create(new { query }));

        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        try
        {
            var responseObject = JsonSerializer.Deserialize<T>(responseString, JsonOptions);
            return responseObject;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize response from GitHub. ResponseString: {responseString}", ex);
        }
    }
}

public class GitHubData
{
    public Data Data { get; set; }

}

public class Data
{
    public Repository Repository { get; set; }
    public Organization Organization { get; set; }
}

public class Repository
{
    public IssueConnection Issues { get; set; }
}

public class IssueConnection
{
    public List<Issue> Nodes { get; set; }
}

public class Issue
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Url { get; set; }
}

public class Organization
{
    public ProjectData ProjectV2 { get; set; }
}

public class ProjectData
{
    public string Id { get; set; }
}
