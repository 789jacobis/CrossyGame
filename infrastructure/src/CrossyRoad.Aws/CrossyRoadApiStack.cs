using Amazon.CDK;
using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AwsApigatewayv2Authorizers;
using Amazon.CDK.AwsApigatewayv2Integrations;
using Constructs;
using ApiHttpMethod = Amazon.CDK.AWS.Apigatewayv2.HttpMethod;

namespace CrossyRoad.Aws;

public sealed class CrossyRoadApiStack : CrossyRoadStack
{
    public Function Handler { get; }

    public HttpApi Api { get; }

    public CrossyRoadApiStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props,
        CrossyRoadDataStack data,
        CrossyRoadIdentityStack identity)
        : base(scope, id, settings, props)
    {
        string artifactPath = FindBackendArtifact();

        var logGroup = new LogGroup(this, "GameBackendLogs", new LogGroupProps
        {
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        Handler = new Function(this, "GameBackend", new FunctionProps
        {
            Runtime = Runtime.DOTNET_10,
            Architecture = Architecture.X86_64,
            Handler = "CrossyRoad.AwsBackend::CrossyRoad.AwsBackend.Function::FunctionHandler",
            Code = Code.FromAsset(artifactPath),
            MemorySize = 256,
            Timeout = Duration.Seconds(10),
            LogGroup = logGroup,
            Environment = new Dictionary<string, string>
            {
                ["RUNS_TABLE_NAME"] = data.RunsTable.TableName,
                ["SCORES_TABLE_NAME"] = data.ScoresTable.TableName
            }
        });

        data.RunsTable.GrantReadWriteData(Handler);
        data.ScoresTable.GrantReadWriteData(Handler);

        var integration = new HttpLambdaIntegration(
            "GameBackendIntegration",
            Handler);
        var iamAuthorizer = new HttpIamAuthorizer();
        Api = new HttpApi(this, "GameApi", new HttpApiProps
        {
            ApiName = $"{settings.ProjectName}-{settings.EnvironmentName}",
            CorsPreflight = new CorsPreflightOptions
            {
                AllowOrigins = ["*"],
                AllowMethods = [CorsHttpMethod.GET, CorsHttpMethod.POST],
                AllowHeaders =
                [
                    "content-type",
                    "authorization",
                    "x-amz-date",
                    "x-amz-security-token",
                    "x-amz-content-sha256"
                ],
                MaxAge = Duration.Hours(1)
            }
        });

        Api.AddRoutes(new AddRoutesOptions
        {
            Path = "/health",
            Methods = [ApiHttpMethod.GET],
            Integration = integration
        });

        foreach ((string path, ApiHttpMethod method) in new[]
        {
            ("/runs", ApiHttpMethod.POST),
            ("/scores", ApiHttpMethod.POST),
            ("/leaderboard", ApiHttpMethod.GET)
        })
        {
            Api.AddRoutes(new AddRoutesOptions
            {
                Path = path,
                Methods = [method],
                Integration = integration,
                Authorizer = iamAuthorizer
            });
        }

        _ = new Policy(this, "GuestApiInvokePolicy", new PolicyProps
        {
            Roles = [identity.GuestRole],
            Statements =
            [
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = ["execute-api:Invoke"],
                    Resources = [Api.ArnForExecuteApi()]
                })
            ]
        });

        _ = new CfnOutput(this, "ApiUrl", new CfnOutputProps
        {
            Value = Api.ApiEndpoint
        });

        _ = new CfnOutput(this, "AwsRegion", new CfnOutputProps
        {
            Value = settings.Region
        });

        var defaultStage = (CfnStage)Api.DefaultStage!.Node.DefaultChild!;
        defaultStage.DefaultRouteSettings = new CfnStage.RouteSettingsProperty
        {
            DetailedMetricsEnabled = true,
            ThrottlingBurstLimit = 20,
            ThrottlingRateLimit = 10
        };
    }

    private static string FindBackendArtifact()
    {
        foreach (string startPath in new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        })
        {
            DirectoryInfo? directory = new(startPath);
            while (directory is not null)
            {
                foreach (string relativePath in new[]
                {
                    Path.Combine(".artifacts", "backend"),
                    Path.Combine("infrastructure", ".artifacts", "backend")
                })
                {
                    string candidate = Path.Combine(
                        directory.FullName,
                        relativePath);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Publish the AWS backend to infrastructure/.artifacts/backend " +
            "before synthesizing.");
    }
}
