#if NETFRAMEWORK
using System.Net;
#endif
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using WindowsSimpleTaskTabBar.Core.Update;

namespace WindowsSimpleTaskTabBar.Services;

/// <summary>What a check found.</summary>
internal enum UpdateCheckOutcome
{
    /// <summary>GitHub could not be reached, or did not answer with a version.</summary>
    Failed,

    /// <summary>The newest release is the one running.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    UpdateAvailable,
}

/// <summary>The answer to one check.</summary>
internal sealed class UpdateCheckResult
{
    public UpdateCheckOutcome Outcome;
    public string LatestTag = string.Empty;
}

/// <summary>
/// Asks GitHub for the newest release and compares it with the version running.
/// </summary>
/// <remarks>
/// The request is made here; deciding what to do with the answer, and getting it back onto the
/// user interface thread, belongs to <c>MainForm</c>, which owns the window. Nothing here throws:
/// every failure is one <see cref="UpdateCheckOutcome.Failed"/>, because the bar is usually
/// started at logon where nobody is watching, and the caller has no more to say about a proxy
/// than about a cable.
///
/// The response is read with <see cref="DataContractJsonSerializer"/>, the same tool the settings
/// file uses. A NuGet package for JSON is deliberately avoided: the application is distributed as
/// a single executable.
/// </remarks>
internal static class UpdateService
{
    private const string Owner = "kaorinstar";
    private const string Repo = "windows-simple-tasktabbar";

    /// <summary>How long to wait for GitHub before giving up.</summary>
    /// <remarks>
    /// The default is 100 seconds, which would leave a request in flight for a minute and a half
    /// after a user who is going to see nothing anyway has stopped caring.
    /// </remarks>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The page a user is sent to. Built from the constants above and never from anything the
    /// network answered: this string is handed to the shell, and a URL taken from a response
    /// would let whatever answered choose what gets opened.
    /// </summary>
    public static string LatestReleaseUrl { get; } =
        "https://github.com/" + Owner + "/" + Repo + "/releases/latest";

    /// <summary>
    /// The version of the running build, as <c>AssemblyInformationalVersion</c> holds it.
    /// </summary>
    /// <remarks>
    /// That attribute reads <c>v0.3.0</c> on a published build, because <c>release.yml</c> stamps
    /// it from the tag, and the plain <c>0.3.0</c> of <c>Directory.Build.props</c> on every other
    /// build. <c>ReleaseVersion</c> accepts both, so nothing here has to know which it got.
    /// </remarks>
    public static string RunningVersion { get; } = ReadRunningVersion();

    /// <summary>
    /// Asks GitHub for the newest release. Blocks, so call it off the user interface thread.
    /// Never throws.
    /// </summary>
    public static UpdateCheckResult Check()
    {
        var result = new UpdateCheckResult();

        try
        {
            string tag = FetchLatestTag();
            if (tag.Length == 0) return result;

            result.LatestTag = tag;
            result.Outcome = ReleaseVersion.IsNewer(tag, RunningVersion)
                ? UpdateCheckOutcome.UpdateAvailable
                : UpdateCheckOutcome.UpToDate;
        }
        catch
        {
            // No network, no name resolution, a proxy that wants credentials, a timeout, a
            // repository that is not readable without one: all the same answer to the user.
            result.Outcome = UpdateCheckOutcome.Failed;
            result.LatestTag = string.Empty;
        }

        return result;
    }

    /// <summary>The tag of the newest release, or empty when the answer held none.</summary>
    private static string FetchLatestTag()
    {
#if NETFRAMEWORK
        // .NET Framework 4.8 negotiates whatever the machine is configured for, which is TLS 1.2
        // on every supported version of Windows. A machine still carrying an old SCHANNEL policy
        // would offer TLS 1.0, which github.com refuses. Guarded, because assigning a protocol
        // the platform does not know throws.
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch (NotSupportedException)
        {
        }
#endif

        using var client = new HttpClient { Timeout = RequestTimeout };

        // GitHub answers 403 to a request with no User-Agent. The version is included so that a
        // request seen in a log says which build made it.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsSimpleTaskTabBar/" + RunningVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var url = new Uri("https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest");

        using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) return string.Empty;

        using Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        var serializer = new DataContractJsonSerializer(typeof(LatestRelease));
        var release = serializer.ReadObject(stream) as LatestRelease;

        return release?.TagName ?? string.Empty;
    }

    private static string ReadRunningVersion()
    {
        try
        {
            Assembly self = typeof(UpdateService).Assembly;
            string informational = self
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational)) return informational;

            return self.GetName().Version?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The one field of the release that is read. Every other member of the response is skipped:
    /// naming only what is used means a change elsewhere in the response cannot break this.
    /// </summary>
    [DataContract]
    internal sealed class LatestRelease
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }
    }
}
