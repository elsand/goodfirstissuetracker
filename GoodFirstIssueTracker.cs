using System.Net.Http.Json;
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
        GithubClient.DefaultRequestHeaders.Add("User-Agent", "good-first-issue-tracker");
        GithubClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    [Function("GoodFirstIssueTracker")]
    public async Task RunAsync([TimerTrigger("0 */10 * * * *", RunOnStartup = true)] TimerInfo timerInfo)
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

        _logger.LogInformation("Found {numRepos} repositories in configuration, adding to project https//github.com/org/{org}/projects/{project}",
            repos.Length, orgAndProjectNumber[0], orgAndProjectNumber[1]);

        var issuesAdded = 0;
        foreach (var repo in repos)
        {
            var repoParts = repo.Split('/');
            var owner = repoParts[0];
            var repoName = repoParts[1];

            var issues = await GetGoodFirstIssues(owner, repoName);

            if (issues is null)
            {
                _logger.LogWarning("Unable to load issues from {owner}/{repoName}", owner, repoName);
                continue;
            }

            foreach (var issue in issues)
            {
                await AddIssueToProject(issue.Id, projectId);
                _logger.LogInformation("Added {issue}", issue.Url);
                issuesAdded++;
            }
        }

        _logger.LogInformation("Added {numIssues} issues to project, next run at {nextRun}", issuesAdded, timerInfo.ScheduleStatus?.Next);
    }

    private static async Task<List<Issue>?> GetGoodFirstIssues(string owner, string repoName)
    {
        var query = $$"""
                      {
                        repository(owner: "{{owner}}", name: "{{repoName}}") {
                          issues(labels: "good first issue", first: 100, states: OPEN) {
                            nodes {
                              id
                              url
                            }
                          }
                        }
                      }
                      """;

        var response = await ExecuteGraphQlQuery<GitHubData>(query);
        var issues = response?.Data.Repository.Issues.Nodes;

        return issues;
    }

    private static async Task<string> GetProjectNodeId(string owner, string projectNumber)
    {
        var query = $$"""
                      {
                        organization(login: "{{owner}}") {
                          projectV2(number: {{projectNumber}}) {
                            id
                          }
                        }
                      }
                      """;

        var response = await ExecuteGraphQlQuery<GitHubData>(query);
        var projectId = response?.Data.Organization.ProjectV2.Id;

        if (projectId is null)
        {
            throw new InvalidOperationException("Failed to fetch project node id from GitHub");
        }

        return projectId;
    }

    private static async Task AddIssueToProject(string issueId, string projectId)
    {
        var mutation = $$"""
                         mutation {
                           addProjectV2ItemById(input: {projectId: "{{projectId}}", contentId: "{{issueId}}"}) {
                             item {
                               id
                             }
                           }
                         }
                         """;

        await ExecuteGraphQlQuery<dynamic>(mutation);
    }

    private static async Task<T?> ExecuteGraphQlQuery<T>(string query)
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
