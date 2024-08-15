namespace Digdir.BDB.GoodFirstIssuesTracker;
// ReSharper disable ClassNeverInstantiated.Global
public class GitHubData
{
    public Data Data { get; set; } = null!;
}

public class Data
{
    public Repository Repository { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}

public class Repository
{
    public IssueConnection Issues { get; set; } = null!;
}

public class IssueConnection
{
    public List<Issue> Nodes { get; set; } = null!;
}

public class Issue
{
    public string Id { get; set; } = null!;
    public string Url { get; set; } = null!;
}

public class Organization
{
    public ProjectData ProjectV2 { get; set; } = null!;
}

public class ProjectData
{
    public string Id { get; set; } = null!;
}
// ReSharper restore ClassNeverInstantiated.Global
