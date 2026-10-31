# Antivirus false positives and signed executables

SharpTS bundles a managed program into a .NET apphost when you compile with
`--target exe`. The resulting executable contains an appended bundle and may
extract runtime content when it starts. Behavioral antivirus scanners can flag
this packaging pattern even when the TypeScript program is harmless.

Keep your security software enabled. Check unexpected detections, verify the
source you compiled, and submit a false-positive report to the scanner vendor
when appropriate. A different bundler does not establish publisher identity.

## Sign a Windows executable

Authenticode records the signing identity and lets consumers detect changes to
the signed content. It can contribute to publisher reputation; signing does not
guarantee that a scanner will accept every new executable.

Set `SHARPTS_SIGN_PASS` through your shell's secret input or CI secret settings,
then compile using your code-signing certificate:

```powershell
sharpts --compile hello.ts -t exe --sign code-signing.pfx
signtool verify /pa /v hello.exe
```

Windows discovers `signtool.exe` on PATH or in Windows SDK signing tools. An
existing certificate in the current user's `My` store can be selected with
`--sign sha1:<40-hex-digit-thumbprint>` instead of a PFX file. The SHA-1 thumbprint
identifies the certificate; the file digest defaults to SHA-256.

`--sign-pass` explicitly overrides `SHARPTS_SIGN_PASS` for PFX/P12 files.
Certificate-store thumbprints do not use the password environment variable.
Prefer the environment variable to keep the password out of shell history. Windows SignTool still
receives a PFX password as a process argument because that is its supported API;
SharpTS redacts passwords from signing diagnostics.

The default RFC 3161 server is `https://timestamp.digicert.com`. Override it with
`--sign-timestamp <url>`. `--sign-digest` accepts `sha256`, `sha384`, or `sha512`.
Signing runs after successful bundling and before packaging can copy the
executable. If the signer rejects the certificate,
password, timestamp service, or tool installation, SharpTS exits nonzero and
preserves the original executable. It does not silently return unsigned success.

Authenticode applies to Windows PE outputs. Linux ELF and macOS Mach-O programs
need their platforms' signing mechanisms. The signing adapter can sign Windows bundles on
Linux/macOS using `osslsigncode`, but SharpTS's current `--target exe` CLI selects
the host platform's executable format.

## File-only development test

For a local signing experiment, OpenSSL can create a one-day test certificate
without installing it in a certificate or trust store:

```powershell
openssl req -x509 -newkey rsa:2048 -sha256 -nodes -keyout test-key.pem -out test-cert.pem -days 1 -subj '/CN=SharpTS local test' -addext 'extendedKeyUsage=codeSigning' -addext 'keyUsage=digitalSignature'
openssl pkcs12 -export -inkey test-key.pem -in test-cert.pem -out test.pfx -passout env:SHARPTS_SIGN_PASS
sharpts --compile hello.ts -t exe --sign test.pfx --sign-timestamp none
osslsigncode verify -CAfile test-cert.pem -in hello.exe
```

`--sign-timestamp none` is for offline tests. The explicit `-CAfile` trusts only
this verification command's test certificate. Windows verification will report
that a self-signed development certificate is untrusted; production distribution
requires a certificate chain trusted by your consumers. Delete the test files
after the experiment, and keep real signing keys in your normal secret storage.

See [Microsoft SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool)
and [osslsigncode](https://github.com/mtrojnar/osslsigncode) for tool installation
and signature verification details.
