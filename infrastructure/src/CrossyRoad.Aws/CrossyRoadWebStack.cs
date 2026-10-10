using Amazon.CDK;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace CrossyRoad.Aws;

public sealed class CrossyRoadWebStack : CrossyRoadStack
{
    public CrossyRoadWebStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props)
        : base(scope, id, settings, props)
    {
        var webBucket = new Bucket(this, "WebAssets", new BucketProps
        {
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            Versioned = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
            AutoDeleteObjects = false,
            LifecycleRules =
            [
                new LifecycleRule
                {
                    NoncurrentVersionExpiration = Duration.Days(30)
                }
            ]
        });

        var distribution = new Distribution(
            this,
            "WebDistribution",
            new DistributionProps
            {
                DefaultRootObject = "index.html",
                DefaultBehavior = new BehaviorOptions
                {
                    Origin = S3BucketOrigin.WithOriginAccessControl(webBucket),
                    ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                    AllowedMethods = AllowedMethods.ALLOW_GET_HEAD_OPTIONS,
                    CachedMethods = CachedMethods.CACHE_GET_HEAD_OPTIONS,
                    CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                    Compress = true,
                    ResponseHeadersPolicy = ResponseHeadersPolicy.SECURITY_HEADERS
                },
                HttpVersion = HttpVersion.HTTP2_AND_3,
                PriceClass = PriceClass.PRICE_CLASS_200,
                EnableLogging = false
            });

        _ = new CfnOutput(this, "WebBucketName", new CfnOutputProps
        {
            Description = "Private S3 bucket containing the Unity Web build.",
            Value = webBucket.BucketName
        });

        _ = new CfnOutput(this, "CloudFrontDistributionId", new CfnOutputProps
        {
            Description = "CloudFront distribution ID used for cache invalidation.",
            Value = distribution.DistributionId
        });

        _ = new CfnOutput(this, "GameUrl", new CfnOutputProps
        {
            Description = "Public HTTPS URL for the game.",
            Value = $"https://{distribution.DistributionDomainName}"
        });
    }
}
