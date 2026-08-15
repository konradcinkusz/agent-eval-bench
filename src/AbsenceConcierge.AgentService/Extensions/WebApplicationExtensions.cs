namespace AbsenceConcierge.AgentService.Extensions;

/// <summary>
/// The pipeline, in the order it runs.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Response headers for the one page this service serves.
    ///
    /// <para>
    /// The content security policy has <b>no <c>unsafe-inline</c></b>, and that is
    /// why the showcase page keeps its CSS and its script in separate files. Inlining
    /// them would have been one fewer request and would have cost the strictest
    /// clause in the policy — which is the trade that quietly happens on most pages,
    /// and the reason a strict CSP is rarer than a CSP.
    /// </para>
    /// <para>
    /// <c>connect-src 'self'</c> is doing real work here rather than being
    /// boilerplate: this page is a demo whose visitors are invited to type into it,
    /// and it means a script that somehow reached the page could not send what they
    /// typed anywhere.
    /// </para>
    /// </summary>
    public static WebApplication UseShowcaseSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            headers["Content-Security-Policy"] =
                "default-src 'none'; "
                + "script-src 'self'; "
                + "style-src 'self'; "
                + "img-src 'self' data:; "
                + "font-src 'self'; "
                + "connect-src 'self'; "
                + "form-action 'none'; "
                + "frame-ancestors 'none'; "
                + "base-uri 'none'";

            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";

            // frame-ancestors above supersedes this for any browser made in the last
            // decade. Kept because the cost is one header and the failure it prevents
            // — clickjacking the approve button — is the one interaction on the page
            // that matters.
            headers["X-Frame-Options"] = "DENY";

            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            await next().ConfigureAwait(false);
        });

        return app;
    }

    /// <summary>
    /// Serves the showcase page.
    ///
    /// <para>
    /// Static files from <c>wwwroot/</c>, and nothing else: no SPA fallback, no
    /// directory browsing, no upload path. The page is one HTML file, one stylesheet
    /// and one script, and the reason it is not a framework is written down in
    /// <c>docs/PRODUCTION.md</c> — a build step for a page with one interaction is a
    /// dependency tree nobody is going to keep patched.
    /// </para>
    /// </summary>
    public static WebApplication MapShowcase(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseDefaultFiles();
        app.UseStaticFiles();

        return app;
    }
}
