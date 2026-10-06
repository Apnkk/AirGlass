// Vendor-side license key generator. NEVER ship this tool or the private key.
//   KeyGen init            -> creates the key pair (once), prints the public key
//   KeyGen issue <email>   -> prints a signed license key for that customer
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

var dir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".airserver-license");
var privatePath = Path.Combine(dir, "private.key");

if (args.Length == 0) return Usage();

switch (args[0])
{
    case "init":
        return Init();
    case "issue" when args.Length >= 2:
        return Issue(args[1]);
    default:
        return Usage();
}

int Usage()
{
    Console.Error.WriteLine("Usage:\n  KeyGen init\n  KeyGen issue <email>");
    return 1;
}

int Init()
{
    if (File.Exists(privatePath))
    {
        Console.WriteLine("Private key already exists (kept): " + privatePath);
    }
    else
    {
        Directory.CreateDirectory(dir);
        using var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(privatePath, Convert.ToBase64String(created.ExportPkcs8PrivateKey()));
        Console.WriteLine("Private key created: " + privatePath);
        Console.WriteLine("BACK IT UP: if you lose it you cannot issue keys for this build anymore.");
    }

    using var key = LoadKey();
    Console.WriteLine("PUBLIC_KEY=" + Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    return 0;
}

int Issue(string email)
{
    email = email.Trim();
    if (email.Length == 0 || email.Contains('|'))
    {
        Console.Error.WriteLine("Invalid email.");
        return 1;
    }

    if (!File.Exists(privatePath))
    {
        Console.Error.WriteLine("No private key. Run 'KeyGen init' first.");
        return 1;
    }

    using var key = LoadKey();
    var payload = Encoding.UTF8.GetBytes($"AS1|{email}|{DateTime.UtcNow:yyyy-MM-dd}");
    var signature = key.SignData(payload, HashAlgorithmName.SHA256);
    Console.WriteLine("AS1-" + Base64Url.EncodeToString(payload) + "." + Base64Url.EncodeToString(signature));
    return 0;
}

ECDsa LoadKey()
{
    var key = ECDsa.Create();
    key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(privatePath).Trim()), out _);
    return key;
}
