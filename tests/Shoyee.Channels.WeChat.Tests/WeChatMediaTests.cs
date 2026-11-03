using System.Text.Json;
using Shoyee.Channels.WeChat;
using Xunit;

namespace Shoyee.Channels.WeChat.Tests;

/// <summary>微信媒体：AES 加解密、密钥解析、类型分派、CDN 白名单。</summary>
public sealed class WeChatMediaTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 16).Select(static i => (byte)i)];

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var plaintext = "你好，微信"u8.ToArray();

        var ciphertext = WeChatMedia.Encrypt(plaintext, Key);
        var recovered = WeChatMedia.Decrypt(ciphertext, Key);

        Assert.Equal(plaintext, recovered);
        Assert.Equal(0, ciphertext.Length % 16);
        Assert.True(ciphertext.Length > plaintext.Length);
    }

    [Fact]
    public void ParseAesKey_AcceptsRawAndHexForms()
    {
        var rawB64 = Convert.ToBase64String(Key);
        var hexB64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(Key).ToLowerInvariant()));

        Assert.Equal(Key, WeChatMedia.ParseAesKey(rawB64));
        Assert.Equal(Key, WeChatMedia.ParseAesKey(hexB64));
        Assert.Throws<FormatException>(() => WeChatMedia.ParseAesKey(Convert.ToBase64String(new byte[7])));
    }

    [Fact]
    public void ResolveAesKey_PrefersBareHex()
    {
        var media = new WeChatMediaRef(
            WeChatMediaKind.Image,
            "q",
            null,
            Convert.ToBase64String(new byte[16]),
            Convert.ToHexString(Key),
            "a.jpg",
            null);

        Assert.Equal(Key, WeChatMedia.ResolveAesKey(media));
    }

    [Fact]
    public void ResolveAesKey_ReturnsNullWhenUnreadable()
    {
        var media = new WeChatMediaRef(WeChatMediaKind.File, "q", null, "not-base64", null, "a.bin", null);

        Assert.Null(WeChatMedia.ResolveAesKey(media));
    }

    [Theory]
    [InlineData("a.JPG", WeChatMedia.UploadTypeImage, WeChatMediaKind.Image)]
    [InlineData("a.webp", WeChatMedia.UploadTypeImage, WeChatMediaKind.Image)]
    [InlineData("a.mp4", WeChatMedia.UploadTypeVideo, WeChatMediaKind.Video)]
    [InlineData("a.png", WeChatMedia.UploadTypeImage, WeChatMediaKind.Image)]
    [InlineData("a.silk", WeChatMedia.UploadTypeFile, WeChatMediaKind.File)]
    [InlineData("a.pdf", WeChatMedia.UploadTypeFile, WeChatMediaKind.File)]
    public void ClassifyByFileName_DispatchesByExtension(
        string fileName,
        int expectedUploadType,
        WeChatMediaKind expectedKind)
    {
        var (uploadType, kind) = WeChatMedia.ClassifyByFileName(fileName);

        Assert.Equal(expectedUploadType, uploadType);
        Assert.Equal(expectedKind, kind);
    }

    [Fact]
    public void EncodeAesKeyForApi_IsBase64OfHexString()
    {
        var encoded = WeChatMedia.EncodeAesKeyForApi(Key);

        var decoded = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(encoded));
        Assert.Equal(Convert.ToHexString(Key).ToLowerInvariant(), decoded);
    }

    [Theory]
    [InlineData("https://novac2c.cdn.weixin.qq.com/c2c/download?x=1", true)]
    [InlineData("https://mmbiz.qpic.cn/a.jpg", true)]
    [InlineData("https://evil.example.com/a.jpg", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("not a url", false)]
    public void IsAllowedCdnUrl_OnlyTrustsWeChatHosts(string url, bool expected)
        => Assert.Equal(expected, WeChatMedia.IsAllowedCdnUrl(url));

    [Fact]
    public void BuildOutboundItem_ImageCarriesEncryptedMediaAndSize()
    {
        var item = WeChatMedia.BuildOutboundItem(
            WeChatMediaKind.Image,
            "enc",
            "key",
            ciphertextSize: 32,
            plaintextSize: 20,
            fileName: "a.jpg",
            rawFileMd5: "md5");

        Assert.Equal(2, item["type"]);
        var image = Assert.IsType<Dictionary<string, object>>(item["image_item"]);
        var media = Assert.IsType<Dictionary<string, object>>(image["media"]);
        Assert.Equal("enc", media["encrypt_query_param"]);
        Assert.Equal(1, media["encrypt_type"]);
        Assert.Equal(32L, image["mid_size"]);
    }

    [Fact]
    public void ParseSendResult_TreatsMissingRetAsSuccess()
    {
        var result = WeChatClient.ParseSendResult(JsonDocument.Parse(
            """{"context_token":"ctx2"}""").RootElement);

        Assert.Equal(WeChatSendStatus.Ok, result.Status);
        Assert.Equal("ctx2", result.ContextToken);
    }

    [Fact]
    public void ParseSendResult_ClassifiesMinusTwoByMessage()
    {
        var closed = WeChatClient.ParseSendResult(JsonDocument.Parse(
            """{"ret":-2,"errmsg":"prepare failed"}""").RootElement);
        var stale = WeChatClient.ParseSendResult(JsonDocument.Parse(
            """{"ret":-2,"errmsg":"unknown error"}""").RootElement);

        Assert.Equal(WeChatSendStatus.WindowClosed, closed.Status);
        Assert.Equal(WeChatSendStatus.SessionExpired, stale.Status);
    }

    [Fact]
    public void ParseSendResult_ClassifiesSessionExpiredAndOtherFailures()
    {
        var expired = WeChatClient.ParseSendResult(JsonDocument.Parse("""{"ret":-14}""").RootElement);
        var failed = WeChatClient.ParseSendResult(JsonDocument.Parse("""{"ret":-99,"errmsg":"boom"}""").RootElement);

        Assert.Equal(WeChatSendStatus.SessionExpired, expired.Status);
        Assert.Equal(WeChatSendStatus.Failed, failed.Status);
        Assert.Equal("boom", failed.Error);
    }
}
