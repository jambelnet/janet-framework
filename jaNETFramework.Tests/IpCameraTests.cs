/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using jaNET.Configuration;
using jaNET.Services;
using jaNET.Servers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

public class IpCameraTests
{
    static readonly byte[] Jpeg = { 0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9 };
    static CameraSetup Setup(string revision = "new") => new("", "Front door", "http://192.168.1.50/snapshot.jpg?channel=1", "auto", 5, true, "viewer", "camera-secret", false, revision);
    sealed class Camera : HttpMessageHandler
    {
        public List<string> Urls = new();
        public Func<HttpResponseMessage> Reply = () => Response(Jpeg, "image/jpeg");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Urls.Add(request.RequestUri!.ToString()); return Task.FromResult(Reply());
        }
        public static HttpResponseMessage Response(byte[] bytes, string type, HttpStatusCode code = HttpStatusCode.OK) {
            var response = new HttpResponseMessage(code) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(type); return response;
        }
    }
    static IpCameraService Service(ISettingsStore settings, Camera camera) => new(settings, _ => new HttpClient(camera, disposeHandler: false));

    [Theory]
    [InlineData("rtsp://192.168.1.50/live")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:pass@192.168.1.50/snapshot.jpg")]
    [InlineData("http://localhost:8080/api/command")]
    [InlineData("http://test.localhost/frame")]
    [InlineData("http://127.0.0.1/frame")]
    [InlineData("http://2130706433/frame")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/frame")]
    [InlineData("http://224.0.0.1/frame")]
    [InlineData("http://100.100.100.200/frame")]
    [InlineData("http://[::1]/frame")]
    [InlineData("http://[::ffff:127.0.0.1]/frame")]
    [InlineData("http://[fe80::1]/frame")]
    [InlineData("http://192.168.1.50/frame#fragment")]
    public void UnsafeAndUnsupportedCameraUrlsAreRejected(string url) => Assert.Throws<ArgumentException>(() => IpCameraService.CameraUri(url));

    [Theory]
    [InlineData("http://192.168.1.50/snapshot.jpg?channel=1")]
    [InlineData("https://10.0.0.40/mjpeg")]
    [InlineData("http://172.16.0.25/image.jpg")]
    [InlineData("http://camera.lan/mjpeg")]
    [InlineData("https://camera.example.org/image.jpg")]
    [InlineData("http://[fd00::40]/snapshot.jpg")]
    public void NormalLanAndHttpsEndpointsAreAccepted(string url) => Assert.Equal(url, IpCameraService.CameraUri(url).AbsoluteUri);

    [Fact]
    public void DnsRebindingAndMappedAddressesCannotReachTheJanetHostOrMetadataServices() {
        var own = new[] { IPAddress.Parse("192.168.1.20") };
        IpCameraService.ValidateAddresses(new[] { IPAddress.Parse("192.168.1.50") }, own);
        foreach (string address in new[] { "127.0.0.1", "169.254.169.254", "::ffff:192.168.1.20", "192.168.1.20" })
            Assert.Throws<HttpRequestException>(() => IpCameraService.ValidateAddresses(new[] { IPAddress.Parse("192.168.1.50"), IPAddress.Parse(address) }, own));
        Assert.Throws<HttpRequestException>(() => IpCameraService.ValidateAddresses(Array.Empty<IPAddress>(), own));
    }

    [Fact]
    public async Task CredentialsAreEncryptedAndNeverExportedToWidgetsOrReturnedAsPasswords() {
        using var app = new TempApp(); var settings = new SettingsStore(app.Paths, app.Log);
        using var service = new IpCameraService(settings); string id = await service.Save(Setup());
        Assert.DoesNotContain("camera-secret", File.ReadAllText(app.Paths.File(SettingsFiles.IpCameras)));
        string json = JsonSerializer.Serialize(await service.View()); Assert.DoesNotContain("camera-secret", json);
        var view = await service.View(); Assert.Equal("", view.Cameras.Single().Password); Assert.True(view.Cameras.Single().PasswordConfigured);
        string widgets = JsonSerializer.Serialize(await service.Widgets());
        Assert.Contains(id, widgets); Assert.DoesNotContain("192.168", widgets); Assert.DoesNotContain("viewer", widgets); Assert.DoesNotContain("camera-secret", widgets);
        Assert.Contains(SettingsFiles.IpCameras, SettingsFiles.All); Assert.DoesNotContain(app.Log.Entries, line => line.Contains("camera-secret"));
    }

    [Fact]
    public async Task BlankPasswordsAreRetainedAndNewHostsCannotSilentlyReceiveSavedCredentials() {
        var settings = new MemorySettingsStore(); using var service = new IpCameraService(settings);
        string id = await service.Save(Setup()); var view = await service.View();
        await service.Save(Setup(view.Revision) with { Id = id, Password = "", Name = "Garden" });
        Assert.Equal("camera-secret", JsonSerializer.Deserialize<CameraStore>(settings.Load(SettingsFiles.IpCameras)![0])!.Cameras.Single().Password);
        view = await service.View();
        await Assert.ThrowsAsync<ArgumentException>(() => service.Save(Setup(view.Revision) with { Id = id, Url = "http://192.168.1.60/snapshot.jpg", Password = "" }));
        await service.Save(Setup(view.Revision) with { Id = id, Url = "http://192.168.1.60/snapshot.jpg", Password = "", ClearPassword = true });
        Assert.False((await service.View()).Cameras.Single().PasswordConfigured);
    }

    [Fact]
    public async Task ConflictsAndInvalidSettingsDoNotOverwriteProfilesAndRemovalOnlyRemovesTheSelectedCamera() {
        var settings = new MemorySettingsStore(); using var service = new IpCameraService(settings);
        string first = await service.Save(Setup()); var view = await service.View();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Save(Setup()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Save(Setup(view.Revision) with { RefreshSeconds = 0 }));
        Assert.Single((await service.View()).Cameras);
        string second = await service.Save(Setup(view.Revision) with { Name = "Back door", ShowOnHome = false });
        Assert.Single(await service.Widgets());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Remove(new(first, view.Revision)));
        await service.Remove(new(first, (await service.View()).Revision));
        Assert.Equal(second, (await service.View()).Cameras.Single().Id); Assert.Empty(await service.Widgets());
        settings.Save(SettingsFiles.IpCameras, "broken");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Save(Setup())); Assert.Equal("broken", settings.Load(SettingsFiles.IpCameras)![0]);
    }

    [Fact]
    public async Task MaximumSavedProfilesAndConcurrentConnectionsAreBounded() {
        var settings = new MemorySettingsStore(); using var camera = new Camera(); using var service = Service(settings, camera);
        var ids = new List<string>();
        for (int i = 0; i < 8; i++) ids.Add(await service.Save(Setup((await service.View()).Revision) with { Name = "Camera " + i }));
        string fullRevision = (await service.View()).Revision;
        await Assert.ThrowsAsync<ArgumentException>(() => service.Save(Setup(fullRevision)));
        var feeds = new List<CameraFeed>();
        try {
            foreach (var id in ids) feeds.Add(await service.Open(id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(ids[0]));
        } finally { foreach (var feed in feeds) feed.Dispose(); }
        using var next = await service.Open(ids[0]); Assert.Equal(Jpeg, await next.Snapshot());
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("image/png")]
    public async Task SnapshotFormatsAreDetectedAndOnlyConfiguredUrlsAreFetched(string type) {
        var settings = new MemorySettingsStore(); using var camera = new Camera();
        byte[] bytes = type == "image/png" ? new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 } : Jpeg;
        camera.Reply = () => Camera.Response(bytes, type);
        using var service = Service(settings, camera); string id = await service.Save(Setup());
        using var feed = await service.Open(id); Assert.Equal("snapshot", feed.Kind); Assert.Equal(bytes, await feed.Snapshot());
        Assert.Equal(new[] { Setup().Url }, camera.Urls);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Open("http://attacker.example/"));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/json")]
    [InlineData("multipart/x-mixed-replace")]
    [InlineData("multipart/x-mixed-replace; boundary=\"bad boundary\"")]
    public async Task PagesScriptsAndMalformedMultipartResponsesAreRejected(string type) {
        var settings = new MemorySettingsStore(); using var camera = new Camera(); camera.Reply = () => Camera.Response(Encoding.UTF8.GetBytes("<script>bad()</script>"), type);
        using var service = Service(settings, camera); string id = await service.Save(Setup());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(id));
    }

    [Fact]
    public async Task AuthenticationRedirectsMismatchesAndOversizedOrInvalidImagesReportErrors() {
        var settings = new MemorySettingsStore(); using var camera = new Camera(); using var service = Service(settings, camera); string id = await service.Save(Setup());
        camera.Reply = () => Camera.Response(Jpeg, "image/jpeg", HttpStatusCode.Unauthorized);
        Assert.Contains("authentication", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(id))).Message);
        camera.Reply = () => Camera.Response(Jpeg, "image/jpeg", HttpStatusCode.Redirect);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(id));
        camera.Reply = () => Camera.Response(Encoding.UTF8.GetBytes("not a picture"), "image/jpeg");
        using (var feed = await service.Open(id)) await Assert.ThrowsAsync<InvalidOperationException>(() => feed.Snapshot());
        camera.Reply = () => { var response = Camera.Response(Jpeg, "image/jpeg"); response.Content.Headers.ContentLength = IpCameraService.MaxSnapshotBytes + 1; return response; };
        using (var feed = await service.Open(id)) await Assert.ThrowsAsync<InvalidOperationException>(() => feed.Snapshot());
        await service.Save(Setup((await service.View()).Revision) with { Id = id, Mode = "mjpeg", Password = "" });
        camera.Reply = () => Camera.Response(Jpeg, "image/jpeg"); await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(id));
    }

    [Fact]
    public async Task MjpegPassesThroughWithoutBufferingAndNormalEndIsTreatedAsDisconnected() {
        var settings = new MemorySettingsStore(); using var camera = new Camera();
        byte[] multipart = Encoding.ASCII.GetBytes("--frame\r\nContent-Type: image/jpeg\r\n\r\nexample\r\n--frame--\r\n");
        camera.Reply = () => Camera.Response(multipart, "multipart/x-mixed-replace; boundary=frame");
        using var service = Service(settings, camera); string id = await service.Save(Setup());
        using var feed = await service.Open(id); Assert.Equal("mjpeg", feed.Kind);
        using var output = new MemoryStream(); await Assert.ThrowsAsync<InvalidOperationException>(() => feed.CopyStream(output, CancellationToken.None));
        Assert.Equal(multipart, output.ToArray());
    }

    sealed class StalledStream : Stream
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditingOrRemovingACameraCancelsItsActiveRead(bool remove) {
        var settings = new MemorySettingsStore(); using var camera = new Camera(); using var input = new StalledStream();
        camera.Reply = () => { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(input) }; response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/x-mixed-replace; boundary=frame"); return response; };
        using var service = Service(settings, camera); string id = await service.Save(Setup()); using var feed = await service.Open(id);
        using var output = new MemoryStream(); var copying = feed.CopyStream(output, CancellationToken.None);
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (remove) await service.Remove(new(id, (await service.View()).Revision));
        else await service.Save(Setup((await service.View()).Revision) with { Id = id, Name = "Changed", Password = "" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copying.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WebApiProtectsSettingsAndFeedsWithoutRunningInstructionsOrLeakingSecrets() {
        using var app = new TempApp(); var config = app.NewConfig(); var settings = new SettingsStore(app.Paths, app.Log);
        int port = Ports.Free(); config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        using var camera = new Camera(); using var service = Service(settings, camera);
        using var server = new WebServer(config, settings, app.Paths, () => throw new InvalidOperationException("No commands allowed"), app.Log, cameras: service);
        server.Start(); using var client = new HttpClient(); string url = $"http://localhost:{port}/api/cameras/";
        using var saved = await client.PostAsJsonAsync(url + "save", Setup()); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        string body = await saved.Content.ReadAsStringAsync(); Assert.DoesNotContain("camera-secret", body); string id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
        using var widgets = await client.GetAsync(url + "widgets"); Assert.True(widgets.Headers.CacheControl!.NoStore); Assert.DoesNotContain("192.168", await widgets.Content.ReadAsStringAsync());
        using var probe = await client.GetAsync(url + id + "/probe"); Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        using var feed = await client.GetAsync(url + id + "/feed"); Assert.Equal(Jpeg, await feed.Content.ReadAsByteArrayAsync()); Assert.Equal("image/jpeg", feed.Content.Headers.ContentType!.MediaType);
        Assert.Equal("same-origin", feed.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        using var attack = new HttpRequestMessage(HttpMethod.Post, url + "remove") { Content = JsonContent.Create(new CameraRemoval(id, (await service.View()).Revision)) };
        attack.Headers.Add("Origin", "https://evil.example.org"); using var denied = await client.SendAsync(attack); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var embed = new HttpRequestMessage(HttpMethod.Get, url + id + "/feed"); embed.Headers.Add("Sec-Fetch-Site", "cross-site"); using var deniedEmbed = await client.SendAsync(embed); Assert.Equal(HttpStatusCode.Forbidden, deniedEmbed.StatusCode);
        using var wrongBody = await client.PostAsync(url + "save", new StringContent("{}")); Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongBody.StatusCode);
        using var getRemove = await client.GetAsync(url + "remove"); Assert.Equal(HttpStatusCode.NotFound, getRemove.StatusCode);
        using var unknown = await client.GetAsync(url + new string('a', 32) + "/feed"); Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var file = await client.GetAsync($"http://localhost:{port}/.ipcameras"); Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        camera.Reply = () => throw new HttpRequestException("camera-secret http://private-camera");
        using var failure = await client.GetAsync(url + id + "/probe"); Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.DoesNotContain("camera-secret", await failure.Content.ReadAsStringAsync()); Assert.DoesNotContain(app.Log.Entries, line => line.Contains("camera-secret"));
        settings.Save(SettingsFiles.WebLogin, "admin\nprivate"); config.Update(new CommUpdate { Authentication = "basic" }); server.Stop(); server.Start();
        using var anonymous = await client.GetAsync(url + "settings"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
