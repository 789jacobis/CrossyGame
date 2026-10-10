using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace CrossyRoad.Aws;

public sealed class CrossyRoadCiStack : CrossyRoadStack
{
    public CrossyRoadCiStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props)
        : base(scope, id, settings, props)
    {
        var provider = new OpenIdConnectProvider(this, "GitHubProvider", new OpenIdConnectProviderProps
        {
            Url = "https://token.actions.githubusercontent.com",
            ClientIds = ["sts.amazonaws.com"]
        });

        var deployRole = new Role(this, "GitHubDeployRole", new RoleProps
        {
            RoleName = $"{settings.ProjectName}-GitHubActions-{settings.EnvironmentName}",
            Description = "Short-lived GitHub Actions role for Cross The Road production CDK deployments.",
            MaxSessionDuration = Duration.Hours(1),
            AssumedBy = new FederatedPrincipal(
                provider.OpenIdConnectProviderArn,
                new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com"
                    },
                    ["StringLike"] = new Dictionary<string, object>
                    {
                        ["token.actions.githubusercontent.com:sub"] =
                            "repo:789jacobis@126335657/CrossyGame@1365267436:" +
                            "ref:refs/heads/main"
                    }
                },
                "sts:AssumeRoleWithWebIdentity")
        });

        string BootstrapRole(string roleType) =>
            $"arn:{Amazon.CDK.Aws.PARTITION}:iam::{Amazon.CDK.Aws.ACCOUNT_ID}:role/" +
            $"cdk-hnb659fds-{roleType}-role-{Amazon.CDK.Aws.ACCOUNT_ID}-{settings.Region}";

        deployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["sts:AssumeRole"],
            Resources =
            [
                BootstrapRole("deploy"),
                BootstrapRole("file-publishing"),
                BootstrapRole("image-publishing"),
                BootstrapRole("lookup")
            ]
        }));

        _ = new CfnOutput(this, "GitHubDeployRoleArn", new CfnOutputProps
        {
            Value = deployRole.RoleArn
        });
    }
}
