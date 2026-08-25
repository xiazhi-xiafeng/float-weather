using System.IO;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;

namespace FloatWeather.Services;

/// <summary>
/// 和风天气 JWT(EdDSA/Ed25519) 认证签名。
/// Header: {alg=EdDSA, kid=凭据ID}; Payload: {sub=项目ID, iat, exp}; 私钥签名。
/// </summary>
public static class QWeatherJwt
{
    /// <summary>生成 JWT token。</summary>
    /// <param name="projectId">项目ID → sub</param>
    /// <param name="credentialId">凭据ID → kid</param>
    /// <param name="privateKeyPem">Ed25519 私钥 PEM</param>
    /// <param name="validSeconds">有效期（最大 86400）</param>
    public static string Build(string projectId, string credentialId, string privateKeyPem, int validSeconds = 3600)
    {
        if (validSeconds > 86400) validSeconds = 86400;

        var header = $"{{\"alg\":\"EdDSA\",\"kid\":\"{credentialId}\"}}";

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{{\"sub\":\"{projectId}\",\"iat\":{now - 30},\"exp\":{now + validSeconds}}}";

        var headerB64 = Base64Url(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signingInput = $"{headerB64}.{payloadB64}";

        var signature = Ed25519Sign(signingInput, privateKeyPem);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static byte[] Ed25519Sign(string data, string privateKeyPem)
    {
        using var reader = new StringReader(privateKeyPem.Trim());
        var pemReader = new PemReader(reader);
        var keyObj = pemReader.ReadObject();

        Ed25519PrivateKeyParameters privateKey = keyObj switch
        {
            Ed25519PrivateKeyParameters p => p,
            AsymmetricCipherKeyPair kp => (Ed25519PrivateKeyParameters)kp.Private,
            _ => throw new InvalidOperationException("无法解析 Ed25519 私钥（应为 -----BEGIN PRIVATE KEY-----）")
        };

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var bytes = Encoding.UTF8.GetBytes(data);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return signer.GenerateSignature();
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}