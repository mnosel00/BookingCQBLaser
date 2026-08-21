using BookingCQBLaser.Infrastructure.ExternalServices.PGateway;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace BookingCQBLaser.Api.Filters
{
    public class HotPayIpWhitelistFilter : IAsyncActionFilter
    {
        private readonly IOptions<HotPayOptions> _hotPayOptions;
        private readonly ILogger<HotPayIpWhitelistFilter> _logger;

        public HotPayIpWhitelistFilter(IOptions<HotPayOptions> hotPayOptions, ILogger<HotPayIpWhitelistFilter> logger)
        {
            _hotPayOptions = hotPayOptions;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var remoteIp = ExtractRemoteIpAddress(context.HttpContext);


            if (!ValidateRemoteIpAddress(remoteIp))
            {
                _logger.LogWarning(
                    "HotPayIpWhitelistFilter: Webhook request REJECTED from untrusted IP '{RemoteIp}'. " +
                    "Returning HTTP 403 Forbidden.",
                    remoteIp ?? "UNKNOWN");
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                return;
            }

            _logger.LogDebug("HotPayIpWhitelistFilter: Request from trusted IP '{RemoteIp}' accepted.", remoteIp);
            await next();
        }

        private string? ExtractRemoteIpAddress(HttpContext httpContext)
        {
            // UseForwardedHeaders (configured in Program.cs) already resolves RemoteIpAddress from
            // X-Forwarded-For when - and only when - the request arrived via the trusted local
            // reverse proxy. Reading the header directly here would let any caller spoof it.
            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogDebug("IP extracted from RemoteIpAddress: {RemoteIp}", remoteIp);
            return remoteIp;
        }


        private bool ValidateRemoteIpAddress(string? remoteIp)
        {
            if (string.IsNullOrEmpty(remoteIp))
            {
                _logger.LogWarning("HotPayIpWhitelistFilter: Remote IP address is null or empty.");
                return false;
            }

            var trustedIps = _hotPayOptions.Value.TrustedWebhookIpAddresses
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (trustedIps.Count == 0)
            {
                _logger.LogWarning(
                    "HotPayIpWhitelistFilter: No trusted HotPay IP addresses configured. Rejecting all webhook requests.");
                return false;
            }

            var isWhitelisted = trustedIps.Contains(remoteIp);

            if (!isWhitelisted)
            {
                _logger.LogWarning(
                    "HotPayIpWhitelistFilter: IP '{RemoteIp}' not in whitelist. Trusted IPs: {TrustedIps}",
                    remoteIp,
                    string.Join(", ", trustedIps));
            }

            return isWhitelisted;
        }
    }
}
