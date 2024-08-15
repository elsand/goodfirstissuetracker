# Good First Issue Tracker

## About
This is a Azure Function app that loops over a pre-configured list of public repositories, finds all open issues with the label "good first issue" and adds it to a preconfigured project.

Written in C#, using net8.0 and Azure functions app v4 (isolated)

## Usage

This is meant to be deployed to a Azure function app. The following configuration keys must be present:

```
GitHub:Token = ghp_<your token here>  # require scopes: project, public_repo, read:org
GitHub:Repos = org1/repo1,org2/repo2
GitHub:Project = org1/123    # project number as it appears in the URL
```
