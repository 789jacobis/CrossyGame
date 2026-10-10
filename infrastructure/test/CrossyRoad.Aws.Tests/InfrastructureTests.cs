using Amazon.CDK;
using Amazon.CDK.Assertions;
using Constructs;
using CrossyRoad.Aws;
using NUnit.Framework;

namespace CrossyRoad.Aws.Tests;

public sealed class InfrastructureTests
{
    [Test]
    public void ProductionSettings_UseTokyoAndExpectedNames()
    {
        DeploymentSettings settings = DeploymentSettings.Production;

        Assert.Multiple(() =>
        {
            Assert.That(settings.EnvironmentName, Is.EqualTo("production"));
            Assert.That(settings.Region, Is.EqualTo("ap-northeast-1"));
            Assert.That(
                settings.StackName("Web"),
                Is.EqualTo("CrossyRoadWebStack-production"));
        });
    }

    [Test]
    public void FromContext_ReadsExplicitConfiguration()
    {
        var app = new App(new AppProps
        {
            Context = new Dictionary<string, object>
            {
                ["projectName"] = "CrossyRoad",
                ["environmentName"] = "production",
                ["region"] = "ap-northeast-1"
            }
        });

        DeploymentSettings settings =
            DeploymentSettings.FromContext(app.Node);

        Assert.Multiple(() =>
        {
            Assert.That(settings.ProjectName, Is.EqualTo("CrossyRoad"));
            Assert.That(settings.EnvironmentName, Is.EqualTo("production"));
            Assert.That(settings.Region, Is.EqualTo("ap-northeast-1"));
        });
    }

    [Test]
    public void EnsureValid_RejectsNonProductionEnvironment()
    {
        DeploymentSettings settings =
            DeploymentSettings.Production with
            {
                EnvironmentName = "development"
            };

        Assert.Throws<ArgumentException>(settings.EnsureValid);
    }

    [Test]
    public void DefineStacks_CreatesExpectedProductionBoundaries()
    {
        var app = new App();

        CrossyRoadStacks stacks = CrossyRoadInfrastructure.DefineStacks(
            app,
            DeploymentSettings.Production);

        Assert.Multiple(() =>
        {
            Assert.That(
                stacks.Data.StackName,
                Is.EqualTo("CrossyRoadDataStack-production"));
            Assert.That(
                stacks.Identity.StackName,
                Is.EqualTo("CrossyRoadIdentityStack-production"));
            Assert.That(
                stacks.Api.StackName,
                Is.EqualTo("CrossyRoadApiStack-production"));
            Assert.That(
                stacks.Web.StackName,
                Is.EqualTo("CrossyRoadWebStack-production"));
            Assert.That(
                stacks.Monitoring.StackName,
                Is.EqualTo("CrossyRoadMonitoringStack-production"));
            Assert.That(
                stacks.Ci.StackName,
                Is.EqualTo("CrossyRoadCiStack-production"));
        });
    }

    [Test]
    public void DefineStacks_PinsRegionAndProtectsAllStacks()
    {
        var app = new App();
        CrossyRoadStacks stacks = CrossyRoadInfrastructure.DefineStacks(
            app,
            DeploymentSettings.Production);

        Stack[] allStacks =
        {
            stacks.Data,
            stacks.Identity,
            stacks.Api,
            stacks.Web,
            stacks.Monitoring,
            stacks.Ci
        };

        Assert.Multiple(() =>
        {
            Assert.That(allStacks, Has.Length.EqualTo(6));
            Assert.That(
                allStacks.All(stack =>
                    stack.Region == "ap-northeast-1"),
                Is.True);
            Assert.That(
                allStacks.All(stack => stack.TerminationProtection),
                Is.True);
        });
    }

    [Test]
    public void WebStack_CreatesPrivateEncryptedVersionedBucket()
    {
        Template template = CreateWebTemplate();

        template.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["BucketEncryption"] = Match.AnyValue(),
            ["PublicAccessBlockConfiguration"] = new Dictionary<string, object>
            {
                ["BlockPublicAcls"] = true,
                ["BlockPublicPolicy"] = true,
                ["IgnorePublicAcls"] = true,
                ["RestrictPublicBuckets"] = true
            },
            ["VersioningConfiguration"] = new Dictionary<string, object>
            {
                ["Status"] = "Enabled"
            }
        });

        template.ResourceCountIs("AWS::S3::Bucket", 1);
    }

    [Test]
    public void WebStack_UsesCloudFrontOacAndHttps()
    {
        Template template = CreateWebTemplate();

        template.ResourceCountIs("AWS::CloudFront::OriginAccessControl", 1);
        template.HasResourceProperties(
            "AWS::CloudFront::Distribution",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["DistributionConfig"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DefaultRootObject"] = "index.html",
                    ["Enabled"] = true,
                    ["HttpVersion"] = "http2and3",
                    ["PriceClass"] = "PriceClass_200",
                    ["DefaultCacheBehavior"] = Match.ObjectLike(
                        new Dictionary<string, object>
                        {
                            ["ViewerProtocolPolicy"] = "redirect-to-https",
                            ["Compress"] = true
                        })
                })
            }));
    }

    [Test]
    public void WebStack_ExportsDeploymentValues()
    {
        Template template = CreateWebTemplate();

        template.HasOutput("WebBucketName", new Dictionary<string, object>());
        template.HasOutput(
            "CloudFrontDistributionId",
            new Dictionary<string, object>());
        template.HasOutput("GameUrl", new Dictionary<string, object>());
    }

    [Test]
    public void DataStack_CreatesProtectedOnDemandTables()
    {
        var app = new App();
        var stack = new CrossyRoadDataStack(
            app,
            "DataStackUnderTest",
            DeploymentSettings.Production,
            new StackProps());
        Template template = Template.FromStack(stack);

        template.ResourceCountIs("AWS::DynamoDB::Table", 2);
        template.HasResourceProperties(
            "AWS::DynamoDB::Table",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["BillingMode"] = "PAY_PER_REQUEST",
                ["DeletionProtectionEnabled"] = true,
                ["PointInTimeRecoverySpecification"] = new Dictionary<string, object>
                {
                    ["PointInTimeRecoveryEnabled"] = true
                }
            }));
        template.HasOutput("RunsTableName", new Dictionary<string, object>());
        template.HasOutput("ScoresTableName", new Dictionary<string, object>());
    }

    [Test]
    public void IdentityStack_CreatesGuestIdentityWithNoDirectDataAccess()
    {
        var app = new App();
        var stack = new CrossyRoadIdentityStack(
            app,
            "IdentityStackUnderTest",
            DeploymentSettings.Production,
            new StackProps());
        Template template = Template.FromStack(stack);

        template.HasResourceProperties(
            "AWS::Cognito::IdentityPool",
            new Dictionary<string, object>
            {
                ["AllowUnauthenticatedIdentities"] = true
            });
        template.ResourceCountIs("AWS::IAM::Role", 1);
        template.ResourceCountIs("AWS::IAM::Policy", 0);
        template.HasOutput("IdentityPoolId", new Dictionary<string, object>());
    }

    [Test]
    public void ApiStack_CreatesDotNetBackendAndIamProtectedRoutes()
    {
        var app = new App();
        var data = new CrossyRoadDataStack(
            app,
            "ApiTestData",
            DeploymentSettings.Production,
            new StackProps());
        var identity = new CrossyRoadIdentityStack(
            app,
            "ApiTestIdentity",
            DeploymentSettings.Production,
            new StackProps());
        var api = new CrossyRoadApiStack(
            app,
            "ApiStackUnderTest",
            DeploymentSettings.Production,
            new StackProps(),
            data,
            identity);
        Template template = Template.FromStack(api);

        template.HasResourceProperties(
            "AWS::Lambda::Function",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["Runtime"] = "dotnet10",
                ["MemorySize"] = 256,
                ["Timeout"] = 10
            }));
        template.HasResourceProperties(
            "AWS::ApiGatewayV2::Stage",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["DefaultRouteSettings"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DetailedMetricsEnabled"] = true,
                    ["ThrottlingBurstLimit"] = 20,
                    ["ThrottlingRateLimit"] = 10
                })
            }));
        template.ResourceCountIs("AWS::ApiGatewayV2::Api", 1);
        template.ResourceCountIs("AWS::ApiGatewayV2::Route", 4);
        template.HasResourceProperties(
            "AWS::IAM::Policy",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Statement"] = Match.ArrayWith(
                    [
                        Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["Action"] = "execute-api:Invoke"
                        })
                    ])
                })
            }));
        template.HasOutput("ApiUrl", new Dictionary<string, object>());
    }

    [Test]
    public void MonitoringStack_CreatesFiveDollarBudgetWithThreeAlerts()
    {
        var app = new App();
        var data = new CrossyRoadDataStack(
            app,
            "MonitoringTestData",
            DeploymentSettings.Production,
            new StackProps());
        var identity = new CrossyRoadIdentityStack(
            app,
            "MonitoringTestIdentity",
            DeploymentSettings.Production,
            new StackProps());
        var api = new CrossyRoadApiStack(
            app,
            "MonitoringTestApi",
            DeploymentSettings.Production,
            new StackProps(),
            data,
            identity);
        var stack = new CrossyRoadMonitoringStack(
            app,
            "MonitoringStackUnderTest",
            DeploymentSettings.Production,
            new StackProps(),
            api);
        Template template = Template.FromStack(stack);

        template.HasResourceProperties(
            "AWS::Budgets::Budget",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["Budget"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["BudgetLimit"] = new Dictionary<string, object>
                    {
                        ["Amount"] = 5,
                        ["Unit"] = "USD"
                    },
                    ["TimeUnit"] = "MONTHLY"
                }),
                ["NotificationsWithSubscribers"] = Match.ArrayWith(
                [
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Notification"] = Match.ObjectLike(
                            new Dictionary<string, object>
                            {
                                ["Threshold"] = 1
                            })
                    }),
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Notification"] = Match.ObjectLike(
                            new Dictionary<string, object>
                            {
                                ["Threshold"] = 3
                            })
                    }),
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Notification"] = Match.ObjectLike(
                            new Dictionary<string, object>
                            {
                                ["Threshold"] = 5
                            })
                    })
                ])
            }));
        template.ResourceCountIs("AWS::SNS::Topic", 1);
        template.ResourceCountIs("AWS::SNS::Subscription", 2);
        template.ResourceCountIs("AWS::CloudWatch::Alarm", 4);
        template.HasOutput(
            "OperationalAlertTopicArn",
            new Dictionary<string, object>());
    }

    [Test]
    public void CiStack_TrustsOnlyMainBranchAndAssumesBootstrapRoles()
    {
        var app = new App();
        var stack = new CrossyRoadCiStack(
            app,
            "CiStackUnderTest",
            DeploymentSettings.Production,
            new StackProps());
        Template template = Template.FromStack(stack);

        template.ResourceCountIs("Custom::AWSCDKOpenIdConnectProvider", 1);
        template.HasResourceProperties(
            "AWS::IAM::Role",
            Match.ObjectLike(new Dictionary<string, object>
            {
                ["RoleName"] = "CrossyRoad-GitHubActions-production",
                ["AssumeRolePolicyDocument"] = Match.ObjectLike(
                    new Dictionary<string, object>
                    {
                        ["Statement"] = Match.ArrayWith(
                        [
                            Match.ObjectLike(new Dictionary<string, object>
                            {
                                ["Action"] = "sts:AssumeRoleWithWebIdentity",
                                ["Condition"] = Match.ObjectLike(
                                    new Dictionary<string, object>
                                    {
                                        ["StringLike"] = Match.ObjectLike(
                                            new Dictionary<string, object>
                                            {
                                                ["token.actions.githubusercontent.com:sub"] =
                                                    "repo:789jacobis@126335657/" +
                                                    "CrossyGame@1365267436:" +
                                                    "ref:refs/heads/main"
                                            })
                                    })
                            })
                        ])
                    })
            }));
        template.HasOutput("GitHubDeployRoleArn", new Dictionary<string, object>());
    }

    private static Template CreateWebTemplate()
    {
        var app = new App();
        var stack = new CrossyRoadWebStack(
            app,
            "WebStackUnderTest",
            DeploymentSettings.Production,
            new StackProps());

        return Template.FromStack(stack);
    }
}
