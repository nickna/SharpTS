using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SharpTS.Runtime.Types;

/// <summary>Matches potential certificate issuers without verifying signatures or building a trust chain.</summary>
internal static class X509IssuerMetadata
{
    internal static bool CheckIssued(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        try
        {
            if (!NamesEqual(certificate.IssuerName.RawData, issuer.SubjectName.RawData)) return false;
            if (!ValidateStandardExtensions(certificate) || !ValidateStandardExtensions(issuer)) return false;
            if (!SignatureKeyMatches(certificate.SignatureAlgorithm.Value, issuer.PublicKey.Oid.Value)) return false;

            var usage = issuer.Extensions["2.5.29.15"];
            if (usage is not null)
            {
                var flags = new X509KeyUsageExtension(usage, usage.Critical).KeyUsages;
                var required = certificate.Extensions["1.3.6.1.5.5.7.1.14"] is null
                    ? X509KeyUsageFlags.KeyCertSign : X509KeyUsageFlags.DigitalSignature;
                if ((flags & required) == 0) return false;
            }

            var authority = certificate.Extensions["2.5.29.35"];
            if (authority is null) return true;
            var reader = new AsnReader(authority.RawData, AsnEncodingRules.DER);
            var fields = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            var keyTag = new Asn1Tag(TagClass.ContextSpecific, 0);
            var namesTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
            var serialTag = new Asn1Tag(TagClass.ContextSpecific, 2);
            if (fields.HasData && fields.PeekTag().HasSameClassAndValue(keyTag))
            {
                var key = fields.ReadOctetString(keyTag);
                var subjectKey = issuer.Extensions["2.5.29.14"];
                if (subjectKey is not null)
                {
                    var identifier = new AsnReader(subjectKey.RawData, AsnEncodingRules.DER);
                    if (!key.AsSpan().SequenceEqual(identifier.ReadOctetString())) return false;
                    identifier.ThrowIfNotEmpty();
                }
            }
            if (fields.HasData && fields.PeekTag().HasSameClassAndValue(namesTag))
            {
                var names = fields.ReadSequence(namesTag);
                var directoryTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
                bool checkedDirectory = false;
                while (names.HasData)
                {
                    if (!checkedDirectory && names.PeekTag().HasSameClassAndValue(directoryTag))
                    {
                        var directory = names.ReadSequence(directoryTag);
                        if (!NamesEqual(directory.ReadEncodedValue().ToArray(), issuer.IssuerName.RawData)) return false;
                        directory.ThrowIfNotEmpty();
                        checkedDirectory = true;
                    }
                    else names.ReadEncodedValue();
                }
            }
            if (fields.HasData && fields.PeekTag().HasSameClassAndValue(serialTag))
            {
                var serial = fields.ReadInteger(serialTag);
                var issuerSerial = new BigInteger(issuer.GetSerialNumber(), isUnsigned: false, isBigEndian: false);
                if (serial != issuerSerial) return false;
            }
            fields.ThrowIfNotEmpty();
            return true;
        }
        catch (AsnContentException) { return false; }
        catch (CryptographicException) { return false; }
    }

    private static bool ValidateStandardExtensions(X509Certificate2 certificate)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in certificate.Extensions)
        {
            var oid = extension.Oid?.Value;
            if (oid is not ("2.5.29.14" or "2.5.29.15" or "2.5.29.19" or "2.5.29.37" or "2.5.29.35"
                or "2.5.29.17" or "1.3.6.1.5.5.7.1.14" or "2.16.840.1.113730.1.1" or "2.5.29.30"
                or "1.3.6.1.5.5.7.1.7" or "1.3.6.1.5.5.7.1.8" or "2.5.29.31")) continue;
            if (!seen.Add(oid)) return false;
            // Read the decoded properties: constructing these wrappers alone does not validate the DER.
            switch (oid)
            {
                case "1.3.6.1.5.5.7.1.14":
                    if (!ValidateProxyExtension(certificate, extension)) return false;
                    break;
                case "2.16.840.1.113730.1.1":
                    var legacyType = new AsnReader(extension.RawData, AsnEncodingRules.DER);
                    legacyType.ReadBitString(out _);
                    legacyType.ThrowIfNotEmpty();
                    break;
                case "1.3.6.1.5.5.7.1.7":
                    ValidateResources(extension.RawData, true);
                    break;
                case "1.3.6.1.5.5.7.1.8":
                    ValidateResources(extension.RawData, false);
                    break;
                case "2.5.29.31":
                    if (!ValidateDistributionPoints(extension.RawData)) return false;
                    break;
                case "2.5.29.30":
                    ValidateNameConstraints(extension.RawData);
                    break;
                case "2.5.29.17":
                    ValidateGeneralNames(extension.RawData);
                    break;
                case "2.5.29.35":
                    // Decode all names, while issuer matching below deliberately uses the first directory name.
                    var authority = new X509AuthorityKeyIdentifierExtension(extension.RawData, extension.Critical);
                    if (authority.RawIssuer is { } rawIssuer)
                    {
                        var names = rawIssuer.ToArray();
                        names[0] = 0x30; // Replace the implicit GeneralNames tag with SEQUENCE.
                        ValidateGeneralNames(names);
                    }
                    break;
                case "2.5.29.14":
                    _ = new X509SubjectKeyIdentifierExtension(extension, extension.Critical).SubjectKeyIdentifier;
                    break;
                case "2.5.29.15":
                    if (new X509KeyUsageExtension(extension, extension.Critical).KeyUsages == 0) return false;
                    break;
                case "2.5.29.19":
                    if (!ValidateBasicConstraints(extension.RawData)) return false;
                    break;
                case "2.5.29.37":
                    _ = new X509EnhancedKeyUsageExtension(extension, extension.Critical).EnhancedKeyUsages;
                    break;
            }
        }
        return true;
    }

    private static bool ValidateBasicConstraints(byte[] encoded, bool requireAuthority = false)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var constraints = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        bool authority = constraints.HasData && constraints.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean)
            && constraints.ReadBoolean();
        bool valid = !constraints.HasData || constraints.ReadInteger().Sign >= 0;
        constraints.ThrowIfNotEmpty();
        return valid && (!requireAuthority || authority);
    }

    private static bool ValidateDistributionPoints(byte[] encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var points = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        var nameTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        var relativeTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
        var reasonsTag = new Asn1Tag(TagClass.ContextSpecific, 1);
        var issuerTag = new Asn1Tag(TagClass.ContextSpecific, 2, true);
        while (points.HasData)
        {
            var point = points.ReadSequence();
            bool hasName = false;
            bool hasIssuer = false;
            if (point.HasData && point.PeekTag().HasSameClassAndValue(nameTag))
            {
                var name = point.ReadSequence(nameTag);
                if (name.PeekTag().HasSameClassAndValue(nameTag))
                    ValidateGeneralNames(WrapNameContents(name.ReadSequence(nameTag), false));
                else
                    _ = CanonicalName(WrapNameContents(name.ReadSetOf(relativeTag), true));
                name.ThrowIfNotEmpty();
                hasName = true;
            }
            if (point.HasData && point.PeekTag().HasSameClassAndValue(reasonsTag)) point.ReadBitString(out _, reasonsTag);
            if (point.HasData && point.PeekTag().HasSameClassAndValue(issuerTag))
            {
                var names = point.ReadSequence(issuerTag);
                hasIssuer = names.HasData;
                ValidateGeneralNames(WrapNameContents(names, false));
            }
            point.ThrowIfNotEmpty();
            if (!hasName && !hasIssuer) return false;
        }
        return true;
    }

    private static byte[] WrapNameContents(AsnReader contents, bool relative)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        if (relative) writer.PushSetOf();
        while (contents.HasData) writer.WriteEncodedValue(contents.ReadEncodedValue().Span);
        if (relative) writer.PopSetOf();
        writer.PopSequence();
        return writer.Encode();
    }

    private static void ValidateResources(byte[] encoded, bool ip)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var resources = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        if (ip)
        {
            while (resources.HasData)
            {
                var family = resources.ReadSequence();
                family.ReadOctetString();
                ValidateResourceChoice(family, true);
                family.ThrowIfNotEmpty();
            }
        }
        else
        {
            for (int kind = 0; kind <= 1; kind++)
            {
                var tag = new Asn1Tag(TagClass.ContextSpecific, kind, true);
                if (!resources.HasData || !resources.PeekTag().HasSameClassAndValue(tag)) continue;
                var choice = resources.ReadSequence(tag);
                ValidateResourceChoice(choice, false);
                choice.ThrowIfNotEmpty();
            }
        }
        resources.ThrowIfNotEmpty();
    }

    private static void ValidateResourceChoice(AsnReader reader, bool ip)
    {
        if (reader.PeekTag().HasSameClassAndValue(Asn1Tag.Null))
        {
            reader.ReadNull();
            return;
        }
        var entries = reader.ReadSequence();
        var scalarTag = ip ? Asn1Tag.PrimitiveBitString : Asn1Tag.Integer;
        while (entries.HasData)
        {
            if (entries.PeekTag().HasSameClassAndValue(scalarTag))
            {
                if (ip) entries.ReadBitString(out _);
                else entries.ReadInteger();
            }
            else
            {
                var range = entries.ReadSequence();
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    if (ip) range.ReadBitString(out _);
                    else range.ReadInteger();
                }
                range.ThrowIfNotEmpty();
            }
        }
    }

    private static void ValidateNameConstraints(byte[] encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var constraints = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        for (int kind = 0; kind <= 1; kind++)
        {
            var tag = new Asn1Tag(TagClass.ContextSpecific, kind, true);
            if (!constraints.HasData || !constraints.PeekTag().HasSameClassAndValue(tag)) continue;
            var subtrees = constraints.ReadSequence(tag);
            while (subtrees.HasData)
            {
                var subtree = subtrees.ReadSequence();
                var name = new AsnWriter(AsnEncodingRules.DER);
                name.PushSequence();
                name.WriteEncodedValue(subtree.ReadEncodedValue().Span);
                name.PopSequence();
                ValidateGeneralNames(name.Encode());
                for (int bound = 0; bound <= 1; bound++)
                {
                    var boundTag = new Asn1Tag(TagClass.ContextSpecific, bound);
                    if (subtree.HasData && subtree.PeekTag().HasSameClassAndValue(boundTag)) subtree.ReadInteger(boundTag);
                }
                subtree.ThrowIfNotEmpty();
            }
        }
        constraints.ThrowIfNotEmpty();
    }

    internal static void ValidateGeneralNames(byte[] encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var names = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        // The BCL's SAN decoder additionally requires IP entries to be 4 or 16 bytes.
        // Reuse the same GeneralNames schema in AKI, which does not impose that restriction.
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var namesTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
        writer.PushSequence();
        writer.PushSequence(namesTag);
        var directoryTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
        while (names.HasData)
        {
            var value = names.ReadEncodedValue();
            var nameReader = new AsnReader(value, AsnEncodingRules.DER);
            if (nameReader.PeekTag().HasSameClassAndValue(directoryTag))
            {
                var directory = nameReader.ReadSequence(directoryTag);
                _ = CanonicalName(directory.ReadEncodedValue().ToArray());
                directory.ThrowIfNotEmpty();
                nameReader.ThrowIfNotEmpty();
            }
            writer.WriteEncodedValue(value.Span);
        }
        writer.PopSequence(namesTag);
        writer.PopSequence();
        _ = new X509AuthorityKeyIdentifierExtension(writer.Encode());
    }

    private static bool ValidateProxyExtension(X509Certificate2 certificate, X509Extension extension)
    {
        var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
        var proxy = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        if (proxy.HasData && proxy.PeekTag().HasSameClassAndValue(Asn1Tag.Integer)) proxy.ReadInteger();
        var policy = proxy.ReadSequence();
        proxy.ThrowIfNotEmpty();
        policy.ReadObjectIdentifier();
        if (policy.HasData) policy.ReadOctetString();
        policy.ThrowIfNotEmpty();
        var constraints = certificate.Extensions["2.5.29.19"];
        if (constraints is not null && ValidateBasicConstraints(constraints.RawData, requireAuthority: true))
            return false;
        return certificate.Extensions["2.5.29.17"] is null && certificate.Extensions["2.5.29.18"] is null;
    }

    private static bool SignatureKeyMatches(string? signature, string? key) => signature switch
    {
        "1.2.840.113549.1.1.2" or "1.2.840.113549.1.1.3" or "1.2.840.113549.1.1.4" or
        "1.2.840.113549.1.1.5" or "1.2.840.113549.1.1.11" or "1.2.840.113549.1.1.12" or
        "1.2.840.113549.1.1.13" or "1.2.840.113549.1.1.14" or "1.2.840.113549.1.1.15" or
        "1.2.840.113549.1.1.16" or "2.16.840.1.101.3.4.3.13" or "2.16.840.1.101.3.4.3.14" or
        "2.16.840.1.101.3.4.3.15" or "2.16.840.1.101.3.4.3.16" => key == "1.2.840.113549.1.1.1",
        "1.2.840.113549.1.1.10" => key is "1.2.840.113549.1.1.1" or "1.2.840.113549.1.1.10",
        "1.2.840.10045.4.1" or "1.2.840.10045.4.3.1" or "1.2.840.10045.4.3.2" or
        "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4" or "2.16.840.1.101.3.4.3.9" or
        "2.16.840.1.101.3.4.3.10" or "2.16.840.1.101.3.4.3.11" or "2.16.840.1.101.3.4.3.12" => key == "1.2.840.10045.2.1",
        "1.2.840.10040.4.3" or "2.16.840.1.101.3.4.3.1" or "2.16.840.1.101.3.4.3.2" or
        "2.16.840.1.101.3.4.3.3" or "2.16.840.1.101.3.4.3.4" or "2.16.840.1.101.3.4.3.5" or
        "2.16.840.1.101.3.4.3.6" or "2.16.840.1.101.3.4.3.7" or "2.16.840.1.101.3.4.3.8" => key == "1.2.840.10040.4.1",
        "1.3.101.112" => key == "1.3.101.112",
        "1.3.101.113" => key == "1.3.101.113",
        _ => false
    };

    private static bool NamesEqual(byte[] first, byte[] second) =>
        CanonicalName(first).AsSpan().SequenceEqual(CanonicalName(second));

    private static byte[] CanonicalName(byte[] encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        while (sequence.HasData)
        {
            var set = sequence.ReadSetOf();
            writer.PushSetOf();
            while (set.HasData)
            {
                var attribute = set.ReadSequence();
                writer.PushSequence();
                writer.WriteObjectIdentifier(attribute.ReadObjectIdentifier());
                var tag = attribute.PeekTag();
                if (tag.TagClass == TagClass.Universal && tag.TagValue is 12 or 19 or 20 or 22 or 26 or 28 or 30)
                {
                    var value = ReadNameString(attribute, tag.TagValue);
                    writer.WriteCharacterString(UniversalTagNumber.UTF8String, NormalizeNameValue(value));
                }
                else writer.WriteEncodedValue(attribute.ReadEncodedValue().Span);
                attribute.ThrowIfNotEmpty();
                writer.PopSequence();
            }
            writer.PopSetOf();
        }
        writer.PopSequence();
        return writer.Encode();
    }

    private static string ReadNameString(AsnReader reader, int tag)
    {
        if (tag != 28) return reader.ReadCharacterString((UniversalTagNumber)tag);
        // UniversalString is big-endian UTF-32; AsnReader does not decode this string type.
        var content = reader.PeekContentBytes();
        reader.ReadEncodedValue();
        try { return new UTF32Encoding(true, false, true).GetString(content.Span); }
        catch (DecoderFallbackException) { throw new AsnContentException("Invalid UniversalString name."); }
    }

    private static string NormalizeNameValue(string value)
    {
        var result = new StringBuilder();
        bool space = false;
        foreach (char ch in value)
        {
            if (ch is ' ' or '\t' or '\r' or '\n' or '\v' or '\f')
            {
                space = result.Length != 0;
                continue;
            }
            if (space) result.Append(' ');
            space = false;
            result.Append(ch is >= 'A' and <= 'Z' ? (char)(ch + ('a' - 'A')) : ch);
        }
        return result.ToString();
    }
}
