using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public class CryptoX509IssuedTests
{
    [Theory, ModeData]
    public void CheckIssuedUsesIssuerMetadataRatherThanSignatureVerification(ExecutionMode mode)
    {
        using var signingKey = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        var subject = new X500DistinguishedName("CN=Issuer");
        var root = new X500DistinguishedName("CN=Root");
        var signer = X509SignatureGenerator.CreateForRSA(signingKey, RSASignaturePadding.Pkcs1);
        using var issuer = Create(subject, root, signingKey, signer, 1,
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, false));
        using var differentKey = Create(subject, root, otherKey, signer, 2,
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, false));
        using var noUsage = Create(subject, root, signingKey, signer, 3);
        using var deniedUsage = Create(subject, root, signingKey, signer, 4,
            new X509BasicConstraintsExtension(true, false, 0, false),
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        using var wrongName = Create(new X500DistinguishedName("CN=Other"), root, signingKey, signer, 5);
        using var child = Create(new X500DistinguishedName("CN=Leaf"), subject, signingKey, signer, 6);
        var source = ProgramFor([child, issuer, differentKey, noUsage, deniedUsage, wrongName], """
            console.log(c[0].checkIssued(c[1]));
            console.log(c[0].checkIssued(c[2]));
            console.log(c[0].verify(c[2].publicKey));
            console.log(c[0].checkIssued(c[3]));
            console.log(c[0].checkIssued(c[4]));
            console.log(c[0].checkIssued(c[5]));
            """);
        Assert.Equal("true\ntrue\nfalse\ntrue\nfalse\nfalse\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedMatchesEachPresentAuthorityIdentifier(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var subject = new X500DistinguishedName("CN=Issuer");
        var root = new X500DistinguishedName("CN=Root");
        var leaf = new X500DistinguishedName("CN=Leaf");
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        using var issuer = Create(subject, root, key, signer, 1,
            new X509SubjectKeyIdentifierExtension("01020304", false));
        using var noIdentifier = Create(subject, root, key, signer, 1);
        using var matching = Create(leaf, subject, key, signer, 2, Authority([1, 2, 3, 4], 1, root));
        using var wrongKey = Create(leaf, subject, key, signer, 3, Authority([5, 6, 7, 8], null, null));
        using var wrongSerial = Create(leaf, subject, key, signer, 4, Authority(null, 9, null));
        using var wrongIssuer = Create(leaf, subject, key, signer, 5, Authority(null, null, subject));
        using var onlySerial = Create(leaf, subject, key, signer, 6, Authority(null, 1, null));
        using var onlyIssuer = Create(leaf, subject, key, signer, 7, Authority(null, null, root));
        var source = ProgramFor([issuer, noIdentifier, matching, wrongKey, wrongSerial, wrongIssuer, onlySerial, onlyIssuer], """
            console.log(c[2].checkIssued(c[0]));
            console.log(c[3].checkIssued(c[0]));
            console.log(c[3].checkIssued(c[1]));
            console.log(c[4].checkIssued(c[0]));
            console.log(c[5].checkIssued(c[0]));
            console.log(c[6].checkIssued(c[0]));
            console.log(c[7].checkIssued(c[0]));
            """);
        Assert.Equal("true\nfalse\ntrue\nfalse\nfalse\ntrue\ntrue\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedCanonicalizesNamesWithoutFoldingNonAscii(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var root = new X500DistinguishedName("CN=Root");
        using var issuer = Create(EncodedName("example issuer", UniversalTagNumber.PrintableString), root, key, signer, 1);
        using var canonical = Create(root, EncodedName("  EXAMPLE   Issuer  ", UniversalTagNumber.UTF8String), key, signer, 2);
        using var different = Create(root, EncodedName("exampleissuer", UniversalTagNumber.UTF8String), key, signer, 3);
        using var nonAsciiIssuer = Create(EncodedName("Émetteur", UniversalTagNumber.UTF8String), root, key, signer, 4);
        using var nonAsciiDifferent = Create(root, EncodedName("émetteur", UniversalTagNumber.UTF8String), key, signer, 5);
        var source = ProgramFor([issuer, canonical, different, nonAsciiIssuer, nonAsciiDifferent], """
            console.log(c[1].checkIssued(c[0]));
            console.log(c[2].checkIssued(c[0]));
            console.log(c[4].checkIssued(c[3]));
            """);
        Assert.Equal("true\nfalse\nfalse\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedRejectsIncompatibleKeyAlgorithmAndMalformedAuthority(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        using var ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedName("CN=Issuer");
        var root = new X500DistinguishedName("CN=Root");
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        using var issuer = Create(subject, root, key, signer, 1);
        var ecRequest = new CertificateRequest(subject, ecKey, HashAlgorithmName.SHA256);
        using var ecIssuer = ecRequest.CreateSelfSigned(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var leaf = Create(root, subject, key, signer, 2);
        using var malformed = Create(root, subject, key, signer, 3,
            new X509Extension("2.5.29.35", [0x30, 0x03, 0x80, 0x02, 0x01], false));
        var source = ProgramFor([issuer, ecIssuer, leaf, malformed], """
            console.log(c[2].checkIssued(c[0]));
            console.log(c[2].checkIssued(c[1]));
            console.log(c[1].checkIssued(c[1]));
            console.log(c[3].checkIssued(c[0]));
            """);
        Assert.Equal("true\nfalse\ntrue\nfalse\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedRejectsInvalidExtensionsOnEitherCertificate(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        var root = new X500DistinguishedName("CN=Root");
        using var issuer = Create(subject, root, key, signer, 1);
        using var leaf = Create(root, subject, key, signer, 2);
        X509Extension[] invalid =
        [
            new("2.5.29.19", [0x05, 0x00], false), // Basic constraints must be a sequence.
            new("2.5.29.19", Convert.FromHexString("30060101FF0201FF"), false), // Negative path length.
            new("2.5.29.17", Convert.FromHexString("3006A40430020500"), false), // Directory name contains an invalid RDN.
            new("2.5.29.35", Convert.FromHexString("3008A106A40430020500"), false), // AKI directory name contains an invalid RDN.
            new("2.5.29.31", [0x05, 0x00], false), // Distribution points must be a sequence.
            new("2.5.29.31", [0x30, 0x02, 0x30, 0x00], false), // Point has neither a name nor a CRL issuer.
            new("2.5.29.31", [0x30, 0x04, 0x30, 0x02, 0xA2, 0x00], false), // Empty CRL issuer is insufficient.
            new("1.3.6.1.5.5.7.1.7", [0x05, 0x00], false), // IP resources must be a sequence.
            new("1.3.6.1.5.5.7.1.8", [0x05, 0x00], true), // AS resources must be a sequence.
            new("2.5.29.30", [0x05, 0x00], false), // Name constraints must be a sequence.
            new("2.5.29.30", [0x30, 0x06, 0xA0, 0x04, 0x30, 0x02, 0x05, 0x00], false), // Invalid subtree base.
            new("2.16.840.1.113730.1.1", [0x05, 0x00], false), // Netscape certificate type must be a bit string.
            new("2.5.29.35", [0x05, 0x00], false), // Authority identifier must be a sequence.
            new("2.5.29.14", [0x05, 0x00], false), // Subject key identifier must be an octet string.
            new("2.5.29.37", [0x05, 0x00], false), // Extended key usage must be a sequence.
            new("2.5.29.15", [0x03, 0x01, 0x00], false) // Empty key usage is invalid.
        ];
        foreach (var extension in invalid)
        {
            using var invalidIssuer = Create(subject, root, key, signer, 3, extension);
            using var invalidLeaf = Create(root, subject, key, signer, 4, extension);
            var source = ProgramFor([issuer, leaf, invalidIssuer, invalidLeaf], """
                console.log(c[1].checkIssued(c[0]));
                console.log(c[1].checkIssued(c[2]));
                console.log(c[3].checkIssued(c[0]));
                """);
            Assert.Equal("true\nfalse\nfalse\n",
                TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
        }
    }

    [Theory, ModeData]
    public void CheckIssuedUsesTheSignedCertificateAlgorithm(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var issuerName = new X500DistinguishedName("CN=Issuer");
        using var issuer = Create(issuerName, issuerName, key, signer, 1);
        using var leaf = Create(new X500DistinguishedName("CN=Leaf"), issuerName, key, signer, 2);
        var certificate = new AsnReader(leaf.RawData, AsnEncodingRules.DER).ReadSequence();
        var signedPart = certificate.ReadEncodedValue();
        certificate.ReadEncodedValue(); // Replace only the outer algorithm identifier.
        var signature = certificate.ReadEncodedValue();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteEncodedValue(signedPart.Span);
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.2.840.10045.4.3.2"); // ECDSA outer label; signed part still names RSA.
        writer.PopSequence();
        writer.WriteEncodedValue(signature.Span);
        writer.PopSequence();
        using var mismatchedOuter = X509CertificateLoader.LoadCertificate(writer.Encode());
        var source = ProgramFor([issuer, leaf, mismatchedOuter], """
            console.log(c[1].checkIssued(c[0]));
            console.log(c[2].checkIssued(c[0]));
            """);
        Assert.Equal("true\ntrue\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedValidatesProxyPermissionsAndExtensionConflicts(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        var leafName = new X500DistinguishedName("CN=Proxy");
        using var digitalIssuer = Create(subject, subject, key, signer, 1,
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        using var certificateIssuer = Create(subject, subject, key, signer, 2,
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, false));
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.3.6.1.5.5.7.21.1");
        writer.PopSequence();
        writer.PopSequence();
        var proxy = new X509Extension("1.3.6.1.5.5.7.1.14", writer.Encode(), true);
        using var ordinary = Create(leafName, subject, key, signer, 3);
        using var valid = Create(leafName, subject, key, signer, 4, proxy);
        using var malformed = Create(leafName, subject, key, signer, 5,
            new X509Extension("1.3.6.1.5.5.7.1.14", [0x05, 0x00], true));
        using var caProxy = Create(leafName, subject, key, signer, 6, proxy,
            new X509BasicConstraintsExtension(true, false, 0, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("proxy.test");
        using var namedProxy = Create(leafName, subject, key, signer, 7, proxy, san.Build());
        var source = ProgramFor([digitalIssuer, certificateIssuer, ordinary, valid, malformed, caProxy, namedProxy], """
            console.log(c[2].checkIssued(c[0]));
            console.log(c[3].checkIssued(c[0]));
            console.log(c[3].checkIssued(c[1]));
            console.log(c[4].checkIssued(c[0]));
            console.log(c[5].checkIssued(c[0]));
            console.log(c[6].checkIssued(c[0]));
            """);
        Assert.Equal("false\ntrue\nfalse\nfalse\nfalse\nfalse\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedUsesFirstAuthorityDirectoryButValidatesAllNames(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        var root = new X500DistinguishedName("CN=Root");
        var wrong = new X500DistinguishedName("CN=Wrong");
        using var issuer = Create(subject, root, key, signer, 1);
        X509Extension Names(bool invalid, params X500DistinguishedName[] values)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            var namesTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
            var directoryTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
            writer.PushSequence();
            writer.PushSequence(namesTag);
            foreach (var value in values)
            {
                writer.PushSequence(directoryTag);
                writer.WriteEncodedValue(value.RawData);
                writer.PopSequence(directoryTag);
            }
            if (invalid) writer.WriteOctetString([1], new Asn1Tag(TagClass.ContextSpecific, 9));
            writer.PopSequence(namesTag);
            writer.PopSequence();
            return new X509Extension("2.5.29.35", writer.Encode(), false);
        }
        using var firstMatches = Create(root, subject, key, signer, 2, Names(false, root, wrong));
        using var secondMatches = Create(root, subject, key, signer, 3, Names(false, wrong, root));
        using var invalidLaterName = Create(root, subject, key, signer, 4, Names(true, root));
        var source = ProgramFor([issuer, firstMatches, secondMatches, invalidLaterName], """
            console.log(c[1].checkIssued(c[0]));
            console.log(c[2].checkIssued(c[0]));
            console.log(c[3].checkIssued(c[0]));
            """);
        Assert.Equal("true\nfalse\nfalse\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedValidatesAlternativeNameSyntaxWithoutRequiringAnIpAddress(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var issuerName = new X500DistinguishedName("CN=Issuer");
        var leafName = new X500DistinguishedName("CN=Leaf");
        using var issuer = Create(issuerName, issuerName, key, signer, 1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("leaf.test");
        using var valid = Create(leafName, issuerName, key, signer, 2, san.Build());
        using var malformed = Create(leafName, issuerName, key, signer, 3,
            new X509Extension("2.5.29.17", [0x05, 0x00], false));
        using var shortIp = Create(leafName, issuerName, key, signer, 4,
            new X509Extension("2.5.29.17", [0x30, 0x05, 0x87, 0x03, 1, 2, 3], false));
        using var unknownName = Create(leafName, issuerName, key, signer, 5,
            new X509Extension("2.5.29.17", [0x30, 0x03, 0x89, 0x01, 0], false));
        var source = ProgramFor([issuer, valid, malformed, shortIp, unknownName], """
            console.log(c[1].checkIssued(c[0]));
            console.log(c[2].checkIssued(c[0]));
            console.log(c[3].checkIssued(c[0]));
            console.log(c[4].checkIssued(c[0]));
            console.log(typeof c[2].subjectAltName);
            console.log(c[3].subjectAltName);
            """);
        Assert.Equal("true\nfalse\ntrue\nfalse\nundefined\nIP Address:<invalid length=3>\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedAcceptsEmptyLegacyCertificateType(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        var leafName = new X500DistinguishedName("CN=Leaf");
        var emptyType = new X509Extension("2.16.840.1.113730.1.1", [0x03, 0x01, 0x00], false);
        using var issuer = Create(subject, subject, key, signer, 1, emptyType);
        using var leaf = Create(leafName, subject, key, signer, 2, emptyType);
        var source = ProgramFor([issuer, leaf], """
            console.log(c[0].checkIssued(c[0]));
            console.log(c[1].checkIssued(c[0]));
            """);
        Assert.Equal("true\ntrue\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedDoesNotApplyValidNameConstraintsAsChainPolicy(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var excluded = new Asn1Tag(TagClass.ContextSpecific, 1, true);
        writer.PushSequence();
        writer.PushSequence(excluded);
        writer.PushSequence();
        writer.WriteCharacterString(UniversalTagNumber.IA5String, "blocked.test", new Asn1Tag(TagClass.ContextSpecific, 2));
        writer.WriteInteger(1, new Asn1Tag(TagClass.ContextSpecific, 0));
        writer.WriteInteger(2, new Asn1Tag(TagClass.ContextSpecific, 1));
        writer.PopSequence();
        writer.PopSequence(excluded);
        writer.PopSequence();
        using var issuer = Create(subject, subject, key, signer, 1,
            new X509Extension("2.5.29.30", writer.Encode(), true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("blocked.test");
        using var leaf = Create(new X500DistinguishedName("CN=Leaf"), subject, key, signer, 2, san.Build());
        using var emptyConstraints = Create(subject, subject, key, signer, 3,
            new X509Extension("2.5.29.30", [0x30, 0x00], true));
        var source = ProgramFor([issuer, leaf, emptyConstraints], """
            console.log(c[1].checkIssued(c[0]));
            console.log(c[1].checkIssued(c[2]));
            """);
        Assert.Equal("true\ntrue\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedAcceptsResourceInheritanceAndRanges(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        X509Extension Resource(bool ip, bool inherit)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            var asTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            writer.PushSequence();
            if (ip)
            {
                writer.PushSequence();
                writer.WriteOctetString([0, 1]);
            }
            else writer.PushSequence(asTag);
            if (inherit) writer.WriteNull();
            else
            {
                writer.PushSequence();
                if (ip) writer.WriteBitString([10], 0);
                else writer.WriteInteger(42);
                writer.PushSequence();
                if (ip)
                {
                    writer.WriteBitString([192, 0, 2, 0], 0);
                    writer.WriteBitString([192, 0, 2, 255], 0);
                }
                else
                {
                    writer.WriteInteger(100);
                    writer.WriteInteger(200);
                }
                writer.PopSequence();
                writer.PopSequence();
            }
            if (ip) writer.PopSequence();
            else writer.PopSequence(asTag);
            writer.PopSequence();
            return new X509Extension(ip ? "1.3.6.1.5.5.7.1.7" : "1.3.6.1.5.5.7.1.8", writer.Encode(), true);
        }
        X509Extension[] resources =
        [
            new("1.3.6.1.5.5.7.1.7", [0x30, 0x00], false),
            new("1.3.6.1.5.5.7.1.8", [0x30, 0x00], true),
            Resource(true, true), Resource(true, false), Resource(false, true), Resource(false, false)
        ];
        foreach (var extension in resources)
        {
            using var issuer = Create(subject, subject, key, signer, 1, extension);
            using var leaf = Create(new X500DistinguishedName("CN=Leaf"), subject, key, signer, 2, extension);
            var source = ProgramFor([issuer, leaf], "console.log(c[1].checkIssued(c[0]));");
            Assert.Equal("true\n", TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
        }
    }

    [Theory, ModeData]
    public void CheckIssuedAcceptsDistributionPointNamesAndCrlIssuers(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var subject = new X500DistinguishedName("CN=Issuer");
        X509Extension Points(int kind)
        {
            if (kind == 3) return PointsWithIssuerOnly(subject);
            var writer = new AsnWriter(AsnEncodingRules.DER);
            var nameTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            var relativeTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
            writer.PushSequence();
            if (kind != 0)
            {
                writer.PushSequence();
                writer.PushSequence(nameTag);
                if (kind == 2)
                {
                    writer.PushSetOf(relativeTag);
                    writer.PushSequence();
                    writer.WriteObjectIdentifier("2.5.4.3");
                    writer.WriteCharacterString(UniversalTagNumber.UTF8String, "CRL");
                    writer.PopSequence();
                    writer.PopSetOf(relativeTag);
                }
                else
                {
                    writer.PushSequence(nameTag);
                    if (kind != 4) writer.WriteCharacterString(UniversalTagNumber.IA5String,
                        "https://example.test/list.crl", new Asn1Tag(TagClass.ContextSpecific, 6));
                    writer.PopSequence(nameTag);
                }
                writer.PopSequence(nameTag);
                writer.WriteBitString([0x40], 6, new Asn1Tag(TagClass.ContextSpecific, 1));
                writer.PopSequence();
            }
            writer.PopSequence();
            return new X509Extension("2.5.29.31", writer.Encode(), false);
        }
        X509Extension PointsWithIssuerOnly(X500DistinguishedName name)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            var issuerTag = new Asn1Tag(TagClass.ContextSpecific, 2, true);
            var directory = new Asn1Tag(TagClass.ContextSpecific, 4, true);
            writer.PushSequence();
            writer.PushSequence();
            writer.PushSequence(issuerTag);
            writer.PushSequence(directory);
            writer.WriteEncodedValue(name.RawData);
            writer.PopSequence(directory);
            writer.PopSequence(issuerTag);
            writer.PopSequence();
            writer.PopSequence();
            return new X509Extension("2.5.29.31", writer.Encode(), false);
        }
        for (int kind = 0; kind <= 4; kind++)
        {
            var extension = Points(kind);
            using var issuer = Create(subject, subject, key, signer, 1, extension);
            using var leaf = Create(new X500DistinguishedName("CN=Leaf"), subject, key, signer, 2, extension);
            var source = ProgramFor([issuer, leaf], "console.log(c[1].checkIssued(c[0]));");
            Assert.Equal("true\n", TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
        }
    }

    [Theory, ModeData]
    public void CheckIssuedAcceptsLargePathLengthsWithoutChainPolicy(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var name = new X500DistinguishedName("CN=Issuer");
        var constraints = new X509Extension("2.5.29.19", Convert.FromHexString("300702050100000000"), false);
        using var issuer = Create(name, name, key, signer, 1, constraints,
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        using var ordinaryIssuer = Create(name, name, key, signer, 2, constraints);
        using var leaf = Create(name, name, key, signer, 3, constraints);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteInteger(4);
        writer.PushSequence();
        writer.WriteObjectIdentifier("1.3.6.1.5.5.7.21.1");
        writer.WriteOctetString([1, 2, 3]);
        writer.PopSequence();
        writer.PopSequence();
        using var proxy = Create(name, name, key, signer, 4, constraints,
            new X509Extension("1.3.6.1.5.5.7.1.14", writer.Encode(), true));
        var source = ProgramFor([issuer, ordinaryIssuer, leaf, proxy], """
            console.log(c[2].checkIssued(c[1]));
            console.log(c[3].checkIssued(c[0]));
            """);
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedRejectsDuplicateCachedExtensions(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var name = new X500DistinguishedName("CN=Issuer");
        (string Oid, string Der)[] extensions =
        [
            ("2.5.29.14", "04020102"), ("2.5.29.15", "03020106"),
            ("2.5.29.19", "3000"), ("2.5.29.37", "3000"),
            ("2.5.29.35", "3000"), ("2.5.29.17", "3000"),
            ("1.3.6.1.5.5.7.1.14", "300C300A06082B06010505071501"),
            ("2.16.840.1.113730.1.1", "030100"), ("2.5.29.30", "3000"),
            ("1.3.6.1.5.5.7.1.7", "3000"), ("1.3.6.1.5.5.7.1.8", "3000"),
            ("2.5.29.31", "3000"), ("1.2.3.4.5", "0500")
        ];
        using var ordinary = Create(name, name, key, signer, 1);
        foreach (var (oid, der) in extensions)
        {
            using var single = Create(name, name, key, signer, 2,
                new X509Extension(oid, Convert.FromHexString(der), false));
            using var duplicate = DuplicateFirstExtension(single);
            var source = ProgramFor([ordinary, single, duplicate], """
                console.log(c[1].checkIssued(c[0]));
                console.log(c[0].checkIssued(c[2]));
                console.log(c[2].checkIssued(c[0]));
                """);
            string expected = oid == "1.2.3.4.5" ? "true\ntrue\ntrue\n" : "true\nfalse\nfalse\n";
            Assert.Equal(expected, TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
        }
    }

    private static X509Certificate2 DuplicateFirstExtension(X509Certificate2 certificate)
    {
        // Signature bytes stay unchanged: checkIssued examines metadata, not the signature.
        var outer = new AsnReader(certificate.RawData, AsnEncodingRules.DER).ReadSequence();
        var tbs = outer.ReadSequence();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var extensionTag = new Asn1Tag(TagClass.ContextSpecific, 3, true);
        writer.PushSequence();
        writer.PushSequence();
        while (tbs.HasData)
        {
            if (!tbs.PeekTag().HasSameClassAndValue(extensionTag))
            {
                writer.WriteEncodedValue(tbs.ReadEncodedValue().Span);
                continue;
            }
            var wrapper = tbs.ReadSequence(extensionTag);
            var extensions = wrapper.ReadSequence();
            var first = extensions.ReadEncodedValue();
            writer.PushSequence(extensionTag);
            writer.PushSequence();
            writer.WriteEncodedValue(first.Span);
            writer.WriteEncodedValue(first.Span);
            while (extensions.HasData) writer.WriteEncodedValue(extensions.ReadEncodedValue().Span);
            writer.PopSequence();
            writer.PopSequence(extensionTag);
            wrapper.ThrowIfNotEmpty();
        }
        writer.PopSequence();
        while (outer.HasData) writer.WriteEncodedValue(outer.ReadEncodedValue().Span);
        writer.PopSequence();
        return X509CertificateLoader.LoadCertificate(writer.Encode());
    }

    [Theory, ModeData]
    public void CheckIssuedPreservesRelativeNameGrouping(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        X500DistinguishedName Name(bool grouped, bool reverse, UniversalTagNumber encoding)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            if (grouped) writer.PushSetOf();
            foreach (var oid in reverse ? new[] { "2.5.4.10", "2.5.4.3" } : new[] { "2.5.4.3", "2.5.4.10" })
            {
                if (!grouped) writer.PushSetOf();
                writer.PushSequence();
                writer.WriteObjectIdentifier(oid);
                writer.WriteCharacterString(encoding, oid == "2.5.4.3" ? "Issuer" : "Company");
                writer.PopSequence();
                if (!grouped) writer.PopSetOf();
            }
            if (grouped) writer.PopSetOf();
            writer.PopSequence();
            return new X500DistinguishedName(writer.Encode());
        }
        var root = new X500DistinguishedName("CN=Root");
        using var issuer = Create(Name(true, false, UniversalTagNumber.PrintableString), root, key, signer, 1);
        using var reordered = Create(root, Name(true, true, UniversalTagNumber.BMPString), key, signer, 2);
        using var separate = Create(root, Name(false, false, UniversalTagNumber.UTF8String), key, signer, 3);
        var source = ProgramFor([issuer, reordered, separate], """
            console.log(c[1].checkIssued(c[0]));
            console.log(c[2].checkIssued(c[0]));
            """);
        Assert.Equal("true\nfalse\n", TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedPreservesLargePositiveSerialNumbers(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var name = new X500DistinguishedName("CN=Issuer");
        byte[][] serials = [[0x80], [0xFF], Convert.FromHexString("0102030405060708090A0B0C0D0E0F1011121314")];
        foreach (var serial in serials)
        {
            var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var issuer = request.Create(name, signer,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero), serial);
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteIntegerUnsigned(serial, new Asn1Tag(TagClass.ContextSpecific, 2));
            writer.PopSequence();
            using var leaf = Create(name, name, key, signer, 1,
                new X509Extension("2.5.29.35", writer.Encode(), false));
            var source = ProgramFor([issuer, leaf], "console.log(c[1].checkIssued(c[0]));");
            Assert.Equal("true\n", TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
        }
    }

    [Theory, ModeData]
    public void CheckIssuedCanonicalizesUniversalStringNames(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier("2.5.4.3");
        writer.WriteEncodedValue(Convert.FromHexString("1C18000000490000007300000073000000750000006500000072"));
        writer.PopSequence();
        writer.PopSetOf();
        writer.PopSequence();
        var universal = new X500DistinguishedName(writer.Encode());
        var root = new X500DistinguishedName("CN=Root");
        using var issuer = Create(universal, root, key, signer, 1);
        using var leaf = Create(root, EncodedName("issuer", UniversalTagNumber.UTF8String), key, signer, 2);
        var source = ProgramFor([issuer, leaf], "console.log(c[1].checkIssued(c[0]));");
        Assert.Equal("true\n", TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CheckIssuedValidatesProxyIssuerAndIssuerAlternativeName(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var name = new X500DistinguishedName("CN=Issuer");
        var proxy = new X509Extension("1.3.6.1.5.5.7.1.14", Convert.FromHexString("300C300A06082B06010505071501"), true);
        using var ordinary = Create(name, name, key, signer, 1);
        using var validProxy = Create(name, name, key, signer, 2, proxy);
        using var namedProxy = Create(name, name, key, signer, 3, proxy,
            new X509Extension("2.5.29.18", [0x30, 0x00], false));
        using var malformedProxy = Create(name, name, key, signer, 4,
            new X509Extension("1.3.6.1.5.5.7.1.14", [0x05, 0x00], false));
        var source = ProgramFor([ordinary, validProxy, namedProxy, malformedProxy], """
            console.log(c[0].checkIssued(c[1]));
            console.log(c[0].checkIssued(c[2]));
            console.log(c[2].checkIssued(c[0]));
            console.log(c[0].checkIssued(c[3]));
            """);
        Assert.Equal("true\nfalse\nfalse\nfalse\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void AbsentSubjectAlternativeNameIsUndefined(ExecutionMode mode)
    {
        using var key = RSA.Create(2048);
        var signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
        var name = new X500DistinguishedName("CN=Issuer");
        using var certificate = Create(name, name, key, signer, 1);
        var source = ProgramFor([certificate], """
            console.log(typeof c[0].subjectAltName);
            console.log(c[0].subjectAltName === undefined);
            console.log(c[0].subjectAltName === null);
            console.log(c[0]["subjectAltName"] === undefined);
            """);
        Assert.Equal("undefined\ntrue\nfalse\ntrue\n",
            TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode));
    }

    private static X500DistinguishedName EncodedName(string value, UniversalTagNumber encoding)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier("2.5.4.3");
        writer.WriteCharacterString(encoding, value);
        writer.PopSequence();
        writer.PopSetOf();
        writer.PopSequence();
        return new X500DistinguishedName(writer.Encode());
    }

    private static X509Certificate2 Create(X500DistinguishedName subject, X500DistinguishedName issuer,
        RSA key, X509SignatureGenerator signer, byte serial, params X509Extension[] extensions)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        foreach (var extension in extensions) request.CertificateExtensions.Add(extension);
        return request.Create(issuer, signer, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero), [serial]);
    }

    private static X509Extension Authority(byte[]? key, byte? serial, X500DistinguishedName? issuer)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        if (key is not null) writer.WriteOctetString(key, new Asn1Tag(TagClass.ContextSpecific, 0));
        if (issuer is not null)
        {
            var names = new Asn1Tag(TagClass.ContextSpecific, 1, true);
            var directory = new Asn1Tag(TagClass.ContextSpecific, 4, true);
            writer.PushSequence(names);
            writer.PushSequence(directory);
            writer.WriteEncodedValue(issuer.RawData);
            writer.PopSequence(directory);
            writer.PopSequence(names);
        }
        if (serial is not null) writer.WriteInteger(serial.Value, new Asn1Tag(TagClass.ContextSpecific, 2));
        writer.PopSequence();
        return new X509Extension("2.5.29.35", writer.Encode(), false);
    }

    private static string ProgramFor(X509Certificate2[] certificates, string body) =>
        "import {X509Certificate} from 'crypto';\nconst c = [" +
        string.Join(",", certificates.Select(c => "new X509Certificate(" + JsonSerializer.Serialize(c.ExportCertificatePem()) + ")")) +
        "];\n" + body;
}
