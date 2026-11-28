using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Buffer type - verifies that Buffer instance methods
/// and properties work correctly in both interpreter and compiler modes.
/// </summary>
public class BufferTests
{
    #region Type Annotation and Basic Operations

    [Theory, ModeData]
    public void CompareReadsVisibleUint8ArrayBytes(ExecutionMode mode)
    {
        const string source = """
            const compare=Buffer.compare, a=new Uint8Array([7,1,9]).subarray(1,2), b=Buffer.of(2);
            console.log(compare(a,b), compare(b,a), compare(a,new Uint8Array([1])));
            try { compare(new Uint16Array([1]),b); } catch(e) { console.log(e.name); }
            """;
        Assert.Equal("-1 1 0\nTypeError\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ReadUInt8ValidatesOffsetsBeforeConversion(ExecutionMode mode)
    {
        const string source = """
            const b=Buffer.of(65,66), read=b.readUInt8;
            console.log(read.call(b),read.call(b,undefined),read.call(b,1));
            for(const x of ['1',null,0.5,NaN,Infinity,-1,2]) {
                try { console.log(read.call(b,x)); } catch(e) {console.log(e.name);}
            }
            """;
        Assert.Equal("65 65 66\nTypeError\nTypeError\nRangeError\nRangeError\nRangeError\nRangeError\nRangeError\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void SearchEncodesStringNeedles(ExecutionMode mode)
    {
        const string source = """
            const b=Buffer.of(65,66,255);
            console.log(b.indexOf('42',0,'hex'), b.includes('QQ==',0,'base64'), b.indexOf('�',0,'latin1'));
            console.log(b.indexOf('42',2,'hex'));
            """;
        Assert.Equal("1 true 2\n-1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReadUInt8SharesFunctionAndUsesReceiver(ExecutionMode mode)
    {
        const string source = """
            const a=Buffer.of(65), b=Buffer.of(66);
            const f=a.readUInt8;
            console.log(f===b.readUint8, f.call(b), a.readUInt8());
            try { f(); } catch(e) { console.log(e.name); }
            """;
        Assert.Equal("true 66 65\nTypeError\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void IsEncodingRecognizesBase64Url(ExecutionMode mode)
    {
        const string source = """
            console.log(Buffer.isEncoding('Base64URL'), Buffer.isEncoding(''), Buffer.isEncoding('unknown'));
            """;
        Assert.Equal("true false false\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ConcatHonorsDestinationLength(ExecutionMode mode)
    {
        const string source = """
            const parts = [Buffer.of(65), Buffer.of(66,67)];
            const concat = Buffer.concat;
            for (const n of [0,1,2,3,4]) console.log(concat(parts,n).toString('hex'));
            """;
        Assert.Equal("\n41\n4142\n414243\n41424300\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_TypeAnnotation_Works(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            console.log(buf.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void Buffer_TypeInference_Works(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from("test");
            console.log(buf.toString());
            console.log(buf.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n4\n", output);
    }

    [Theory, ModeData]
    public void Buffer_FromTypedArrayConvertsEachElementAndCopiesTheView(ExecutionMode mode)
    {
        const string source = """
            import { Buffer as B } from 'node:buffer';
            const words = new Uint16Array([0x1234, 0x2345, 0x3456]);
            const view = words.subarray(1);
            const copy = B.from(view);
            view[0] = 0;
            console.log(copy.toString('hex'), Buffer.copyBytesFrom(view).length);
            console.log(B.from(new Float64Array([257.5, -1.5, NaN, 4294967555])).toString('hex'));
            const big: any = new BigInt64Array(1);
            try { B.from(big); } catch (e) { console.log(e.name); }
            console.log(B.from(new BigInt64Array(0) as any).length);
            """;
        Assert.Equal("4556 4\n01ff0003\nTypeError\n0\n",
            TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Buffer_FromArrayBufferSharesTheBackingBytes(ExecutionMode mode)
    {
        const string source = """
            const backing = new ArrayBuffer(2);
            const bytes = new Uint8Array(backing);
            bytes[0] = 7;
            const shared = Buffer.from(backing);
            const copied = Buffer.from(bytes);
            bytes[0] = 11;
            shared[1] = 19;
            console.log(shared[0], bytes[1], copied[0], copied[1]);
            """;

        Assert.Equal("11 19 7 0\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_FromArrayBufferHonorsViewBounds(ExecutionMode mode)
    {
        const string source = """
            const backing = new ArrayBuffer(5);
            const bytes = new Uint8Array(backing);
            bytes.set([1, 2, 3, 4, 5]);
            const view = Buffer.from(backing, 1, 3);
            console.log(view.length, view.byteOffset, view.byteLength, view[0], view[2], view.toString('hex'));
            bytes[1] = 9;
            view[1] = 7;
            view.writeUInt8(15, 2);
            console.log(view.toString('hex'), bytes[1], bytes[2], bytes[3], bytes[4]);
            const from = Buffer.from;
            for (const args of [[2], ['2', undefined], [0, null], [5, 0], [1, -3], [1.6],
                [1.6, 1.5], [6], [1, 8], [-1]]) {
                try { console.log(from(backing, ...args).length); }
                catch (error) { console.log(error.name, error.code); }
            }
            """;
        Assert.Equal("3 1 3 2 4 020304\n09070f 9 7 15 5\n3\n3\n0\n0\n0\n4\n1\n" +
            "RangeError ERR_BUFFER_OUT_OF_BOUNDS\nRangeError ERR_BUFFER_OUT_OF_BOUNDS\nRangeError undefined\n",
            TestHarness.RunModules(new Dictionary<string, string> { ["main.js"] = source }, "main.js", mode));
    }

    [Theory, ModeData]
    public void Buffer_ViewWritesAndConstructionRespectBackingBounds(ExecutionMode mode)
    {
        const string source = """
            const ab = new ArrayBuffer(4);
            new Uint8Array(ab).set([1, 2, 3, 4]);
            const whole = Buffer.from(ab);
            const view = Buffer.from(ab, 1, 3);
            view.fill(whole);
            try { view.write('X', -1); } catch (error) { console.log(error.name, error.code); }
            console.log(whole.toString('hex'));
            const length = { valueOf() { structuredClone(ab, { transfer: [ab] }); return 1; } };
            try { Buffer.from(ab, 0, length); } catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("RangeError ERR_OUT_OF_RANGE\n01010203\ntrue\n",
            TestHarness.RunModules(new Dictionary<string, string> { ["main.js"] = source }, "main.js", mode));
    }

    [Theory, ModeData]
    public void Buffer_FromResolvesTheCurrentReceiverBeforeEvaluatingArguments(ExecutionMode mode)
    {
        const string source = """
            const root = globalThis;
            const original = root.Buffer;
            console.log(Buffer.from("6869", "hex").toString());
            const replacement = {
                get from() {
                    console.log("get");
                    return function (value) { return this === replacement && value === "ascii"; };
                }
            };
            root.Buffer = replacement;
            function reset() { console.log("arg"); root.Buffer = original; return "ascii"; }
            console.log(Buffer.from(...[reset()]));
            console.log(Buffer.from("restored").toString());
            function* callFrom() { return Buffer.from(yield "waiting"); }
            root.Buffer = replacement;
            const call = callFrom();
            console.log(call.next().value);
            root.Buffer = original;
            console.log(call.next("ascii").value);
            """;
        Assert.Equal("hi\nget\narg\ntrue\nrestored\nget\nwaiting\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_FromThrowsTypeErrorForUnknownStringEncodings(ExecutionMode mode)
    {
        const string source = """
            for (const text of ['abc', '']) {
                try { Buffer.from(text, 'No-Such-Encoding'); console.log('accepted'); }
                catch (error) { console.log(error instanceof TypeError, error.name, error.code, error.message); }
            }
            for (const encoding of [undefined, null, 99, '', new String('hex'), 'UtF-8']) {
                console.log(Buffer.from('é', encoding).toString('hex'));
            }
            console.log(Buffer.from([97], 'No-Such-Encoding').toString());
            """;
        const string unknown = "true TypeError ERR_UNKNOWN_ENCODING Unknown encoding: No-Such-Encoding\n";
        Assert.Equal(unknown + unknown + "c3a9\nc3a9\nc3a9\nc3a9\nc3a9\nc3a9\na\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_ByteLengthFallsBackToUtf8ForUnknownEncodings(ExecutionMode mode)
    {
        const string source = """
            const byteLength = Buffer.byteLength;
            console.log(byteLength.length);
            console.log(Buffer.byteLength('é🙂', 'no-such-encoding'));
            console.log(globalThis.Buffer.byteLength('é🙂', 'No-Such-Encoding'));
            console.log(byteLength('é🙂'), byteLength('é🙂', ''), byteLength('é🙂', null));
            console.log(byteLength('é🙂', 'LaTiN1'), byteLength('é🙂', 'UcS-2'));
            console.log(byteLength('YQ==', 'base64'), byteLength('616263', 'HEX'));
            const encoding = { [Symbol.toPrimitive](hint) { console.log(hint); return 'hex'; } };
            console.log(byteLength('616263', encoding));
            console.log(byteLength(Buffer.from('é'), encoding), byteLength('', encoding));
            """;
        Assert.Equal("2\n6\n6\n6 6 6\n3 6\n1 3\ndefault\n3\n2 0\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_ByteLengthUsesEncodedLengthRules(ExecutionMode mode)
    {
        const string source = """
            for (const encoding of ['base64', 'BaSe64Url']) {
                console.log(Buffer.byteLength('YQ==Yg==', encoding), Buffer.byteLength('YQ==', encoding));
                console.log(Buffer.byteLength('Y W J j', encoding), Buffer.byteLength('=', encoding));
            }
            console.log(Buffer.byteLength('61x2', 'hex'), Buffer.byteLength('61x', 'HEX'));
            """;
        Assert.Equal("4 1\n5 0\n4 1\n5 0\n2 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_ByteLengthAcceptsArrayBuffersAndViews(ExecutionMode mode)
    {
        const string source = """
            import { Buffer as ImportedBuffer } from 'node:buffer';
            const buffer = new ArrayBuffer(8);
            const shared = new SharedArrayBuffer(10);
            const words = new Uint16Array(buffer, 2, 3);
            const view = new DataView(buffer, 1, 2);
            const sharedView = new DataView(shared, 3, 5);
            const bigInts = new BigInt64Array(2);
            const precise: number = Buffer.byteLength(words);
            console.log(Buffer.byteLength(buffer), Buffer.byteLength(shared), precise, Buffer.byteLength(view));
            console.log(ImportedBuffer.byteLength(sharedView), ImportedBuffer.byteLength(bigInts));
            console.log(ImportedBuffer.byteLength(new Uint8Array(0)), ImportedBuffer.byteLength(new ArrayBuffer(0)));
            """;
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Equal("8 10 6 2\n5 16\n0 0\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Buffer_ByteLengthAcceptsStandardLibraryBinaryTypes(ExecutionMode mode)
    {
        const string source = """
            import { Buffer as ImportedBuffer } from 'node:buffer';
            function measure(buffer: ArrayBuffer, shared: SharedArrayBuffer, words: Uint16Array, view: DataView): number {
                return ImportedBuffer.byteLength(buffer) + ImportedBuffer.byteLength(shared)
                     + ImportedBuffer.byteLength(words) + ImportedBuffer.byteLength(view);
            }
            console.log(ImportedBuffer.byteLength(new Uint16Array(3)),
                        ImportedBuffer.byteLength(new DataView(new ArrayBuffer(2))));
            """;
        var options = TypeScriptProgramOptions.Default with { Lib = ["lib.es2022.d.ts", "lib.dom.d.ts"] };
        Assert.Equal("6 2\n", TestHarness.RunWithLibraries(source, mode, options));
    }

    [Theory, ModeData]
    public void Buffer_ByteLengthChecksBinaryBrandsAndIgnoresEncoding(ExecutionMode mode)
    {
        const string source = """
            const byteLength = Buffer.byteLength;
            const buffer = new ArrayBuffer(8);
            const words = new Uint16Array(buffer, 2, 3);
            const view = new DataView(buffer, 1, 2);
            const encoding = { [Symbol.toPrimitive]() { throw new Error('encoding must not be coerced'); } };
            console.log(byteLength(buffer, encoding), byteLength(words, encoding), byteLength(view, encoding));
            structuredClone(buffer, { transfer: [buffer] });
            console.log(byteLength(buffer), byteLength(words));
            for (const value of [view, { byteLength: 20 }]) {
                try { byteLength(value); console.log('accepted'); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            """;
        Assert.Equal("8 6 2\n0 0\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Buffer_ByteLengthDistinguishesSameShapedBinaryDeclarations(bool libraryFirst)
    {
        const string declarations = """
            import { Buffer as ImportedBuffer } from 'node:buffer';
            namespace Local {
                export interface ArrayBuffer {
                    readonly byteLength: number;
                    slice(begin?: number, end?: number): ArrayBuffer;
                }
            }
            """;
        const string library = "function library(value: ArrayBuffer) { return ImportedBuffer.byteLength(value); }";
        const string local = "function local(value: Local.ArrayBuffer) { return ImportedBuffer.byteLength(value); }";
        string source = declarations + "\n" + (libraryFirst ? library + "\n" + local : local + "\n" + library);
        var options = TypeScriptProgramOptions.Default with { Lib = ["lib.es5.d.ts"] };

        var error = Assert.Throws<TypeCheckDiagnosticException>(() =>
            TestHarness.RunWithLibraries(source, ExecutionMode.Interpreted, options));
        var diagnostic = Assert.Single(error.Diagnostics);
        Assert.Equal("TS2345", diagnostic.TsCode);
        Assert.Equal(libraryFirst ? 9 : 8, diagnostic.Line);
    }

    [Theory, ModeData]
    public void Buffer_ByteLengthObservesAnArrayBufferOwnGetter(ExecutionMode mode)
    {
        const string source = """
            const value = new ArrayBuffer(4);
            const sentinel = { length: 99 };
            let reads = 0;
            Object.defineProperty(value, 'byteLength', {
                configurable: true,
                get() { reads++; return this === value ? sentinel : -1; }
            });
            const byteLength = Buffer.byteLength;
            console.log(Buffer.byteLength(value) === sentinel, byteLength(value) === sentinel, reads);
            delete value.byteLength;
            console.log(byteLength(value), reads);
            """;
        Assert.Equal("true true 2\n4 2\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_FromDecodesBase64Permissively(ExecutionMode mode)
    {
        const string source = """
            for (const encoding of ['BaSe64', 'base64url']) {
                for (const text of ['Y!WJj', 'Y W\tJ\nj', 'YQ', 'YWI', 'YQ==Yg==', 'YWJjA',
                                    'Y', '!@#', '', '+///', '-___', '\u0159WJj', 'YQ\u013dYg', 'Y\ud83d\ude00WJj']) {
                    const bytes = Buffer.from(text, encoding);
                    console.log(bytes.length + ':' + bytes.toString('hex'));
                }
            }
            try { atob('Y!WJj'); console.log('accepted'); }
            catch { console.log('rejected'); }
            """;
        const string decoded = "3:616263\n3:616263\n1:61\n2:6162\n1:61\n3:616263\n" +
                               "0:\n0:\n0:\n3:fbffff\n3:fbffff\n3:616263\n1:61\n0:\n";
        Assert.Equal(decoded + decoded + "rejected\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_FromHexKeepsCompletePairsBeforeTheFirstInvalidDigit(ExecutionMode mode)
    {
        const string source = """
            const from = Buffer.from;
            for (const text of ['616', 'aB09', '61x262', '61 62', '6a-62', '6', 'x161', '']) {
                const bytes = from(text, 'HEX');
                console.log(bytes.length + ':' + bytes.toString('hex'));
            }
            console.log(Buffer.alloc(3, '616', 'hex').toString('hex'));
            """;
        Assert.Equal("1:61\n2:ab09\n1:61\n1:61\n1:6a\n0:\n0:\n0:\n616161\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_Alloc_Works(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(5);
            console.log(buf.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void Buffer_AllocResolvesAndRetainsTheCurrentReceiver(ExecutionMode mode)
    {
        const string source = """
            const original = globalThis.Buffer;
            const replacement = {
                get alloc() {
                    console.log('get');
                    return function (size) { return this === replacement ? size : -1; };
                }
            };
            globalThis.Buffer = replacement;
            function restore() { console.log('arg'); globalThis.Buffer = original; return 42; }
            console.log(Buffer.alloc(...[restore()]));
            console.log(Buffer.alloc(3.9).length, original.alloc(2).length);
            globalThis.Buffer = replacement;
            function* callAlloc() { return Buffer.alloc(yield 'waiting'); }
            const call = callAlloc();
            console.log(call.next().value);
            globalThis.Buffer = original;
            console.log(call.next(17).value);
            """;
        Assert.Equal("get\narg\n42\n3 2\nget\nwaiting\n17\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_AllocRepeatsFillBytes(ExecutionMode mode)
    {
        const string source = """
            const bytes = new Uint8Array([9, 65, 66, 8]);
            const alloc = Buffer.alloc;
            console.log(Buffer.alloc(3, 'ab').toString());
            console.log(alloc(5, '6162', 'hex').toString());
            console.log(Buffer.alloc(3, Buffer.from('xy')).toString());
            console.log(alloc(5, new Uint8Array(bytes.buffer, 1, 2)).toString());
            console.log(Buffer.alloc(3, 257).toString('hex'), alloc(2, true).toString('hex'));
            console.log(alloc(2, 4294967361).toString(), alloc(2, -1).toString('hex'));
            console.log(alloc(2).toString('hex'), alloc(2, undefined).toString('hex'), alloc(0, 'ab').length);
            """;
        Assert.Equal("aba\nababa\nxyx\nABABA\n010101 0101\nAA ffff\n0000 0000 0\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_AllocValidatesNonemptyFillPatternsAndStringEncodings(ExecutionMode mode)
    {
        const string source = """
            for (const [fill, encoding] of [
                [Buffer.alloc(0), undefined],
                [new Uint8Array(0), undefined],
                ['ab', 'unknown-encoding'],
                ['', 'unknown-encoding'],
                ['zz', 'hex']
            ]) {
                try { Buffer.alloc(3, fill, encoding); console.log('accepted'); }
                catch (error) { console.log(error.name); }
                console.log(Buffer.alloc(0, fill, encoding).length);
            }
            console.log(Buffer.alloc(2, '').toString('hex'), Buffer.alloc(2, 'a', '').toString('hex'));
            console.log(Buffer.alloc(2, 7, 'unknown-encoding').toString('hex'));
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("TypeError\n0\n", 5)) + "0000 6161\n0707\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_Length_Property(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello world");
            console.log(buf.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    #endregion

    #region toString Method

    [Theory, ModeData]
    public void Buffer_ToString_Default(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            console.log(buf.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ToString_WithEncoding(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            console.log(buf.toString("utf8"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region slice Method

    [Theory, ModeData]
    public void Buffer_Slice_WithBothArgs(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello world");
            const sliced = buf.slice(0, 5);
            console.log(sliced.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Slice_StartOnly(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello world");
            const sliced = buf.slice(6);
            console.log(sliced.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("world\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Slice_NoArgs(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("test");
            const sliced = buf.slice();
            console.log(sliced.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Slice_NegativeStart(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hello");
            const sliced = buf.slice(-2);
            console.log(sliced.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("lo\n", output);
    }

    #endregion

    #region copy Method

    [Theory, ModeData]
    public void Buffer_Copy_Basic(ExecutionMode mode)
    {
        var source = """
            const src: Buffer = Buffer.from("hello");
            const dest: Buffer = Buffer.alloc(5);
            const copied = src.copy(dest);
            console.log(copied);
            console.log(dest.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\nhello\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Copy_WithOffsets(ExecutionMode mode)
    {
        var source = """
            const src: Buffer = Buffer.from("hello world");
            const dest: Buffer = Buffer.alloc(5);
            const copied = src.copy(dest, 0, 6, 11);
            console.log(copied);
            console.log(dest.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\nworld\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Copy_Partial(ExecutionMode mode)
    {
        var source = """
            const src: Buffer = Buffer.from("hello");
            const dest: Buffer = Buffer.alloc(3);
            const copied = src.copy(dest, 0, 0, 3);
            console.log(copied);
            console.log(dest.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\nhel\n", output);
    }

    #endregion

    #region compare Method

    [Theory, ModeData]
    public void Buffer_Compare_Less(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("abc");
            const b: Buffer = Buffer.from("abd");
            console.log(a.compare(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Compare_Equal(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("abc");
            const b: Buffer = Buffer.from("abc");
            console.log(a.compare(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Compare_Greater(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("abd");
            const b: Buffer = Buffer.from("abc");
            console.log(a.compare(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Compare_DifferentLengths(ExecutionMode mode)
    {
        var source = """
            const short: Buffer = Buffer.from("ab");
            const long: Buffer = Buffer.from("abc");
            console.log(short.compare(long));
            console.log(long.compare(short));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n1\n", output);
    }

    #endregion

    #region equals Method

    [Theory, ModeData]
    public void Buffer_Equals_True(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("hello");
            const b: Buffer = Buffer.from("hello");
            console.log(a.equals(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Equals_False_DifferentContent(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("hello");
            const b: Buffer = Buffer.from("world");
            console.log(a.equals(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Equals_False_DifferentLength(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("hello");
            const b: Buffer = Buffer.from("hi");
            console.log(a.equals(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region fill Method

    [Theory, ModeData]
    public void Buffer_Fill_WithNumber(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(5);
            buf.fill(65);
            console.log(buf.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("AAAAA\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Fill_WithString(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(6);
            buf.fill("XY");
            console.log(buf.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("XYXYXY\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Fill_WithRange(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(5);
            buf.fill(88, 1, 4);
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            console.log(buf.readUInt8(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n88\n0\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Fill_ReturnsThis(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(3).fill(66);
            console.log(buf.toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("BBB\n", output);
    }

    #endregion

    #region write Method

    [Theory, ModeData]
    public void Buffer_Write_Basic(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(10);
            const written = buf.write("hello");
            console.log(written);
            console.log(buf.slice(0, 5).toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\nhello\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Write_WithOffset(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(10);
            buf.write("XX", 0);
            buf.write("YY", 5);
            console.log(buf.slice(0, 2).toString());
            console.log(buf.slice(5, 7).toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("XX\nYY\n", output);
    }

    #endregion

    #region readUInt8 Method

    [Theory, ModeData]
    public void Buffer_ReadUInt8_Basic(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("AB");
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("65\n66\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadUInt8_DefaultOffset(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("X");
            console.log(buf.readUInt8());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("88\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadUInt8_InExpression(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("AB");
            const sum = buf.readUInt8(0) + buf.readUInt8(1);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("131\n", output);
    }

    #endregion

    #region writeUInt8 Method

    [Theory, ModeData]
    public void Buffer_WriteUInt8_Basic(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(2);
            const pos1 = buf.writeUInt8(255, 0);
            const pos2 = buf.writeUInt8(128, 1);
            console.log(pos1);
            console.log(pos2);
            console.log(buf.readUInt8(0));
            console.log(buf.readUInt8(1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n255\n128\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteUInt8_DefaultOffset(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.alloc(1);
            buf.writeUInt8(42);
            console.log(buf.readUInt8(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region toJSON Method

    [Theory, ModeData]
    public void Buffer_ToJSON_Structure(ExecutionMode mode)
    {
        var source = """
            const buf: Buffer = Buffer.from("hi");
            const json = buf.toJSON();
            console.log(json.type);
            console.log(json.data.length);
            console.log(json.data[0]);
            console.log(json.data[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Buffer\n2\n104\n105\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Theory, ModeData]
    public void Buffer_MethodChaining(ExecutionMode mode)
    {
        var source = """
            const result = Buffer.alloc(4).fill(68).slice(1, 3);
            console.log(result.toString());
            console.log(result.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("DD\n2\n", output);
    }

    [Theory, ModeData]
    public void Buffer_AsFunctionParameter(ExecutionMode mode)
    {
        var source = """
            function getLength(buf: Buffer): number {
                return buf.length;
            }
            console.log(getLength(Buffer.from("test")));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory, ModeData]
    public void Buffer_AsFunctionReturnType(ExecutionMode mode)
    {
        var source = """
            function createBuffer(): Buffer {
                return Buffer.alloc(3).fill(67);
            }
            console.log(createBuffer().toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("CCC\n", output);
    }

    [Theory, ModeData]
    public void Buffer_InArray(ExecutionMode mode)
    {
        var source = """
            const buffers: Buffer[] = [
                Buffer.from("one"),
                Buffer.from("two")
            ];
            console.log(buffers[0].toString());
            console.log(buffers[1].toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("one\ntwo\n", output);
    }

    [Theory, ModeData]
    public void Buffer_InConditional(ExecutionMode mode)
    {
        var source = """
            function getBuffer(useA: boolean): Buffer {
                if (useA) {
                    return Buffer.from("AAA");
                }
                return Buffer.from("BBB");
            }
            console.log(getBuffer(true).toString());
            console.log(getBuffer(false).toString());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("AAA\nBBB\n", output);
    }

    [Theory, ModeData]
    public void Buffer_CompareResultInExpression(ExecutionMode mode)
    {
        var source = """
            const a: Buffer = Buffer.from("a");
            const b: Buffer = Buffer.from("b");
            const isLess = a.compare(b) < 0;
            console.log(isLess);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Multi-byte Read Tests

    [Theory, ModeData]
    public void Buffer_ReadInt8_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([127, 128, 255, 0]);
            console.log(buf.readInt8(0));   // 127
            console.log(buf.readInt8(1));   // -128
            console.log(buf.readInt8(2));   // -1
            console.log(buf.readInt8(3));   // 0
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("127\n-128\n-1\n0\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadUInt16LE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(120, 0);
            buf.writeUInt8(86, 1);
            buf.writeUInt8(52, 2);
            buf.writeUInt8(18, 3);
            console.log(buf.readUInt16LE(0));  // 22136
            console.log(buf.readUInt16LE(2));  // 4660
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("22136\n4660\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadUInt16BE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(18, 0);
            buf.writeUInt8(52, 1);
            buf.writeUInt8(86, 2);
            buf.writeUInt8(120, 3);
            console.log(buf.readUInt16BE(0));  // 4660
            console.log(buf.readUInt16BE(2));  // 22136
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4660\n22136\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadUInt32LE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(120, 0);
            buf.writeUInt8(86, 1);
            buf.writeUInt8(52, 2);
            buf.writeUInt8(18, 3);
            console.log(buf.readUInt32LE(0));  // 305419896
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("305419896\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadUInt32BE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(18, 0);
            buf.writeUInt8(52, 1);
            buf.writeUInt8(86, 2);
            buf.writeUInt8(120, 3);
            console.log(buf.readUInt32BE(0));  // 305419896
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("305419896\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadInt16LE_Signed(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(2);
            buf.writeUInt8(255, 0);
            buf.writeUInt8(255, 1);
            console.log(buf.readInt16LE(0));  // -1
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadInt32LE_Signed(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt8(255, 0);
            buf.writeUInt8(255, 1);
            buf.writeUInt8(255, 2);
            buf.writeUInt8(255, 3);
            console.log(buf.readInt32LE(0));  // -1
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadFloatLE_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeFloatLE(3.14, 0);
            const val = buf.readFloatLE(0);
            console.log(Math.abs(val - 3.14) < 0.001);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Buffer_ReadDoubleLE_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeDoubleLE(3.141592653589793, 0);
            const val = buf.readDoubleLE(0);
            console.log(Math.abs(val - 3.141592653589793) < 0.0000001);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Multi-byte Write Tests

    [Theory, ModeData]
    public void Buffer_WriteInt8_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(2);
            buf.writeInt8(-1, 0);
            buf.writeInt8(127, 1);
            console.log(buf.readUInt8(0));  // 255
            console.log(buf.readUInt8(1));  // 127
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n127\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteUInt16LE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            const next = buf.writeUInt16LE(4660, 0);
            buf.writeUInt16LE(22136, next);
            console.log(buf.readUInt8(0));  // 52
            console.log(buf.readUInt8(1));  // 18
            console.log(buf.readUInt8(2));  // 120
            console.log(buf.readUInt8(3));  // 86
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("52\n18\n120\n86\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteUInt16BE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt16BE(4660, 0);
            buf.writeUInt16BE(22136, 2);
            console.log(buf.readUInt8(0));  // 18
            console.log(buf.readUInt8(1));  // 52
            console.log(buf.readUInt8(2));  // 86
            console.log(buf.readUInt8(3));  // 120
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("18\n52\n86\n120\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteUInt32_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeUInt32LE(305419896, 0);  // 0x12345678
            buf.writeUInt32BE(305419896, 4);  // 0x12345678
            console.log(buf.readUInt32LE(0));
            console.log(buf.readUInt32BE(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("305419896\n305419896\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteInt16_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeInt16LE(-1000, 0);
            buf.writeInt16BE(-1000, 2);
            console.log(buf.readInt16LE(0));
            console.log(buf.readInt16BE(2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1000\n-1000\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteInt32_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeInt32LE(-100000, 0);
            buf.writeInt32BE(-100000, 4);
            console.log(buf.readInt32LE(0));
            console.log(buf.readInt32BE(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-100000\n-100000\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteFloat_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeFloatLE(3.14, 0);
            buf.writeFloatBE(2.71, 4);
            const val1 = buf.readFloatLE(0);
            const val2 = buf.readFloatBE(4);
            console.log(Math.abs(val1 - 3.14) < 0.001);
            console.log(Math.abs(val2 - 2.71) < 0.001);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteDouble_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(16);
            buf.writeDoubleLE(Math.PI, 0);
            buf.writeDoubleBE(Math.E, 8);
            console.log(buf.readDoubleLE(0) === Math.PI);
            console.log(buf.readDoubleBE(8) === Math.E);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Write_ReturnsNextOffset(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(10);
            let offset = 0;
            offset = buf.writeUInt8(1, offset);
            offset = buf.writeUInt16LE(2, offset);
            offset = buf.writeUInt32LE(3, offset);
            console.log(offset);  // 1 + 2 + 4 = 7
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    #endregion

    #region Search Method Tests

    [Theory, ModeData]
    public void Buffer_IndexOf_ByteValue(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 3, 6]);
            console.log(buf.indexOf(3));     // 2
            console.log(buf.indexOf(3, 3));  // 5
            console.log(buf.indexOf(99));    // -1
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n5\n-1\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Includes_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5]);
            console.log(buf.includes(3));    // true
            console.log(buf.includes(99));   // false
            console.log(buf.includes(1, 1)); // false (starts after 1)
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    #endregion

    #region Swap Method Tests

    [Theory, ModeData]
    public void Buffer_Swap16_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4]);
            buf.swap16();
            console.log(buf.readUInt8(0));  // 2
            console.log(buf.readUInt8(1));  // 1
            console.log(buf.readUInt8(2));  // 4
            console.log(buf.readUInt8(3));  // 3
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1\n4\n3\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Swap32_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
            buf.swap32();
            console.log(buf.readUInt8(0));  // 4
            console.log(buf.readUInt8(1));  // 3
            console.log(buf.readUInt8(2));  // 2
            console.log(buf.readUInt8(3));  // 1
            console.log(buf.readUInt8(4));  // 8
            console.log(buf.readUInt8(5));  // 7
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n3\n2\n1\n8\n7\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Swap64_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
            buf.swap64();
            console.log(buf.readUInt8(0));  // 8
            console.log(buf.readUInt8(1));  // 7
            console.log(buf.readUInt8(6));  // 2
            console.log(buf.readUInt8(7));  // 1
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n7\n2\n1\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Swap_Chaining(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3, 4]).swap16().swap16();
            console.log(buf.readUInt8(0));  // 1 (back to original)
            console.log(buf.readUInt8(1));  // 2
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    #endregion

    #region Endianness Tests

    [Theory, ModeData]
    public void Buffer_Endianness_Conversion(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(4);
            buf.writeUInt32LE(305419896, 0);
            console.log(buf.readUInt32BE(0).toString(16));  // 78563412
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("78563412\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Mixed_Endianness(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeUInt32LE(305419896, 0);
            buf.writeUInt32BE(287454020, 4);
            console.log(buf.readUInt32LE(0).toString(16));
            console.log(buf.readUInt32BE(0).toString(16));
            console.log(buf.readUInt32LE(4).toString(16));
            console.log(buf.readUInt32BE(4).toString(16));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12345678\n78563412\n44332211\n11223344\n", output);
    }

    #endregion

    #region BigInt Tests

    [Theory, ModeData]
    public void Buffer_ReadBigInt64LE_Basic(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeUInt8(1, 0);
            buf.writeUInt8(0, 1);
            buf.writeUInt8(0, 2);
            buf.writeUInt8(0, 3);
            buf.writeUInt8(0, 4);
            buf.writeUInt8(0, 5);
            buf.writeUInt8(0, 6);
            buf.writeUInt8(0, 7);
            console.log(buf.readBigInt64LE(0));  // 1n
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1n\n", output);
    }

    [Theory, ModeData]
    public void Buffer_WriteBigInt64LE_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(8);
            buf.writeBigInt64LE(12345n, 0);
            const val = buf.readBigInt64LE(0);
            console.log(val === 12345n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Buffer_BigInt_RoundTrip(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.alloc(16);
            buf.writeBigInt64LE(12345n, 0);
            buf.writeBigInt64BE(67890n, 8);
            console.log(buf.readBigInt64LE(0));
            console.log(buf.readBigInt64BE(8));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12345n\n67890n\n", output);
    }

    #endregion

    #region Issue #45 — compiled Buffer index access

    [Theory, ModeData]
    public void Buffer_IndexAccess_ReturnsByteValue(ExecutionMode mode)
    {
        // Regression for #45: `randomBytes(n)[i]` returned $Undefined in
        // compiled mode because EmitGetIndex had no case for $Buffer.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { randomBytes } from 'crypto';
                const bytes = randomBytes(4);
                console.log(bytes.length);
                for (let i = 0; i < bytes.length; i++) {
                    const b = bytes[i];
                    console.log(typeof b, b >= 0 && b <= 255);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        var lines = output.TrimEnd('\n').Split('\n');
        Assert.Equal("4", lines[0]);
        for (int i = 1; i <= 4; i++)
            Assert.Equal("number true", lines[i]);
    }

    [Theory, ModeData]
    public void Buffer_IndexAccess_NumericOperations(ExecutionMode mode)
    {
        // Regression for #45: `bytes[i] % n` threw InvalidCastException
        // because $Undefined couldn't convert to IConvertible.
        var source = """
            const buf = Buffer.from([10, 20, 30, 255]);
            console.log(buf[0] % 3);
            console.log(buf[3] + 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n256\n", output);
    }

    [Theory, ModeData]
    public void Buffer_IndexAssign_StoresMaskedByte(ExecutionMode mode)
    {
        // Matches Node.js semantics: assignment is masked to 0xFF.
        var source = """
            const buf = Buffer.alloc(3);
            buf[0] = 42;
            buf[1] = 300;  // 300 & 0xFF = 44
            buf[2] = -1;   // -1 & 0xFF = 255
            console.log(buf[0], buf[1], buf[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42 44 255\n", output);
    }

    [Theory, ModeData]
    public void Buffer_SearchEmptyNeedleIncludesEndOfView(ExecutionMode mode)
    {
        const string source = """
            const b = Buffer.from(new ArrayBuffer(5), 1, 3);
            const empty = Buffer.alloc(0);
            for (const offset of [-4, -1, 0, 2, 3, 4])
                console.log(b.indexOf('', offset), b.indexOf(empty, offset), b.includes('', offset));
            console.log(empty.indexOf('', 5), b.indexOf(0, -1), b.includes(0, 3));
            """;
        Assert.Equal("0 0 true\n2 2 true\n0 0 true\n2 2 true\n3 3 true\n3 3 true\n0 2 false\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void Buffer_CompiledReadUInt8MethodUsesCallSiteReceiver()
    {
        const string source = """
            function method(value: any) { return value.readUInt8; }
            const a = Buffer.of(65);
            const b = Buffer.of(66, 67);
            const f = method(a);
            console.log(typeof f, f === method(b), f === b.readUint8);
            console.log(f.call(b), f.apply(b, [1]));
            try { f(); } catch (e) { console.log('invalid receiver'); }
            """;
        Assert.Equal("function true true\n66 67\ninvalid receiver\n", TestHarness.RunCompiled(source));
    }

    [Theory, ModeData]
    public void Buffer_ArrayBufferOwnerAndViewBoundsSurviveTransfer(ExecutionMode mode)
    {
        const string source = """
            const ab = new ArrayBuffer(4);
            const whole = Buffer.from(ab);
            const view = Buffer.from(ab, 1, 2);
            view[0] = 65;
            console.log(whole.buffer === ab, view.buffer === ab, whole[1], view.byteOffset);
            structuredClone(ab, {transfer:[ab]});
            view[0] = 66;
            console.log(view.buffer === ab, whole.length, view.byteOffset, view.byteLength, view[0]);
            const owned = Buffer.alloc(2);
            console.log(owned.buffer === owned.buffer, owned.buffer.byteLength);
            """;
        Assert.Equal("true true 65 1\ntrue 0 0 0 undefined\ntrue 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Buffer_IndexAccess_OutOfRange_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            const buf = Buffer.from([1, 2, 3]);
            console.log(buf[10]);
            console.log(typeof buf[-1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\n", output);
    }

    #endregion

    #region buffer module exports (#1160)

    [Theory, ModeData]
    public void BufferModule_StaticFunctionsCanBeSavedAndCalled(ExecutionMode mode)
    {
        const string source = """
            const {Buffer: B} = require('node:buffer');
            for (const key of ['of','allocUnsafe','allocUnsafeSlow','concat','copyBytesFrom','compare','isEncoding'])
                console.log(typeof B[key], B[key] === B[key]);
            const {of, allocUnsafe, allocUnsafeSlow, concat, copyBytesFrom, compare, isEncoding} = B;
            const first = of(65, 66);
            console.log(first.toString(), allocUnsafe(3).length, allocUnsafeSlow(4).length);
            console.log(concat([first, of(67)]).toString(), compare(first, of(65, 66)));
            console.log(copyBytesFrom(new Uint8Array([10, 11]), 1).toString('hex'), isEncoding('UTF-8'), isEncoding(''));
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("function true\n", 7)) + "AB 3 4\nABC 0\n0b true false\n",
            TestHarness.RunModules(new Dictionary<string, string> { ["main.cjs"] = source }, "main.cjs", mode));
    }

    [Theory, ModeData]
    public void BufferModule_RenamedImportReadsNumericAndSparseArrays(ExecutionMode mode)
    {
        const string source = """
            import { Buffer as B } from 'node:buffer';
            console.log(B.from([65]).length, B.from([65]).toString('hex'));
            const sparse = [65, 66];
            sparse[3] = 68;
            const copy = B.from(sparse);
            sparse[0] = 0;
            console.log(copy.toString('hex'));
            """;
        Assert.Equal("1 41\n41420044\n",
            TestHarness.RunModules(new Dictionary<string, string> { ["main.mjs"] = source }, "main.mjs", mode));
    }

    [Theory, ModeData]
    public void BufferModule_ImportedConstructorCanCallFromThroughAnAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Buffer as ImportedBuffer } from 'node:buffer';
                console.log(typeof ImportedBuffer, ImportedBuffer.from('hi').toString());
                """
        };
        Assert.Equal("function hi\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void BufferModule_NamedImportsKeepTheirConstructorAfterGlobalReplacement(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Buffer, Buffer as ImportedBuffer } from 'node:buffer';
                const root = globalThis as any;
                root.Buffer = { from() { return 'replacement'; } };
                console.log(typeof Buffer, typeof ImportedBuffer, ImportedBuffer === Buffer);
                console.log(Buffer.from('hi').toString(), ImportedBuffer.from('hi').toString(), root.Buffer.from('hi'));
                """
        };
        Assert.Equal("function function true\nhi hi replacement\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void BufferModule_AtobBtoa_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { atob, btoa } from 'buffer';
                const encoded = btoa('hello world');
                console.log(encoded === 'aGVsbG8gd29ybGQ=');
                console.log(atob(encoded) === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void BufferModule_AtobUsesForgivingBase64(ExecutionMode mode)
    {
        const string source = """
            import { atob as moduleAtob } from 'node:buffer';
            for (const decode of [(text: string) => moduleAtob(text), (text: string) => atob(text)]) {
                for (const text of ['YQ', 'YWI', '+///', 'Y R\t\r\n\f', 'YQ= =\f', ' \t\r\n\f']) {
                    console.log('hex:' + Buffer.from(decode(text), 'latin1').toString('hex'));
                }
                for (const text of ['YQ-_', '-___', 'YQ=', 'YQ===', 'YQ==Yg==', 'Y', 'YQ\v', 'YQ\u00a0']) {
                    try { decode(text); console.log('accepted'); }
                    catch (error) { console.log(error.name, error.code, typeof error.code); }
                }
            }
            """;
        const string decoded = "hex:61\nhex:6162\nhex:fbffff\nhex:61\nhex:61\nhex:\n";
        var rejected = string.Concat(Enumerable.Repeat("InvalidCharacterError 5 number\n", 8));
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Equal(decoded + rejected + decoded + rejected, TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void BufferModule_BtoaRejectsCharactersOutsideLatin1(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { btoa as moduleBtoa } from 'node:buffer';
                for (const encode of [(text: string) => moduleBtoa(text), (text: string) => btoa(text)]) {
                    console.log('base64:' + encode(''));
                    console.log(encode('\u0000\u0080\u00ff'));
                    for (const text of ['\u0100', 'ok\uD800', '\uD83D\uDE00']) {
                        try { encode(text); console.log('accepted'); }
                        catch (error) { console.log(error.name, error.code, typeof error.code); }
                    }
                }
                """
        };
        const string result = "base64:\nAID/\nInvalidCharacterError 5 number\nInvalidCharacterError 5 number\nInvalidCharacterError 5 number\n";
        Assert.Equal(result + result, TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void BufferModule_GlobalAtobBtoa(ExecutionMode mode)
    {
        // atob/btoa are also globals (no import).
        var source = """
            console.log(btoa('SharpTS'));
            console.log(atob(btoa('SharpTS')) === 'SharpTS');
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("U2hhcnBUUw==\ntrue\n", output);
    }

    [Theory, ModeData]
    public void BufferModule_GlobalBase64HelpersAreMutableFunctionValues(ExecutionMode mode)
    {
        const string source = """
            const decode = atob;
            const encode = globalThis['btoa'];
            console.log(typeof globalThis.atob, decode === globalThis.atob, decode.name, decode.length);
            console.log(typeof btoa, encode === btoa, encode.name, encode.length);
            console.log(decode('YQ'), encode('a'), encode(null));
            const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'atob');
            console.log(descriptor.value === decode, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            globalThis.atob = value => 'replacement:' + value;
            console.log(atob('YQ'), decode('YQ'));
            """;
        Assert.Equal("function true atob 1\nfunction true btoa 1\na YQ== bnVsbA==\ntrue true true true\nreplacement:YQ a\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void BufferModule_Base64DistinguishesOmittedInputFromUndefined(ExecutionMode mode)
    {
        const string source = """
            for (const convert of [atob, btoa]) {
                try { convert(...[]); }
                catch (error) { console.log(error instanceof TypeError, error.name, error.code, error.message); }
            }
            try { atob(undefined); }
            catch (error) { console.log(error.name, error.code); }
            let extra = 0;
            console.log(btoa(undefined, ++extra), btoa(null), extra);
            """;
        const string missing = "true TypeError ERR_MISSING_ARGS The \"input\" argument must be specified\n";
        Assert.Equal(missing + missing + "InvalidCharacterError 5\ndW5kZWZpbmVk bnVsbA== 1\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void BufferModule_InvalidCharactersThrowDomException(ExecutionMode mode)
    {
        const string source = """
            const D = globalThis.DOMException;
            console.log(typeof D, D === DOMException, D.name, Reflect.get(D, 'name'));
            for (const convert of [() => atob('!'), () => btoa('\u0100')]) {
                try { convert(); console.log('accepted'); }
                catch (error) {
                    console.log(error instanceof D, error instanceof Error, error.constructor === D,
                        Object.getPrototypeOf(error) === D.prototype, error.constructor.name, error.name, error.code);
                }
            }
            console.log(Object.getPrototypeOf(D.prototype) === Error.prototype, D.INVALID_CHARACTER_ERR);
            const manual = new D(null, 'AbortError');
            const readCode = Object.getOwnPropertyDescriptor(D.prototype, 'code').get;
            console.log(manual instanceof D, manual.name, manual.message === 'null', readCode.call(manual));
            console.log(Object.prototype.toString.call(manual), D.prototype[Symbol.toStringTag]);
            console.log(new D().name, new D().code);
            try { readCode.call({}); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        const string branded = "true true true true DOMException InvalidCharacterError 5\n";
        Assert.Equal("function true DOMException DOMException\n" + branded + branded +
            "true 5\ntrue AbortError true 20\n[object DOMException] DOMException\nError 0\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void BufferModule_IsUtf8IsAscii(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { isUtf8, isAscii } from 'buffer';
                console.log(isUtf8(Buffer.from('héllo')));
                console.log(isAscii(Buffer.from('héllo')));
                console.log(isAscii(Buffer.from('hello')));

                const bytes = new Uint8Array([255, 195, 169]);
                const view = new Uint8Array(bytes.buffer, 1, 2);
                console.log(isUtf8(view), isAscii(view), isUtf8(bytes.buffer), isAscii(bytes.buffer));
                const shared = new SharedArrayBuffer(2);
                console.log(isUtf8(shared), isAscii(shared));
                console.log(isUtf8(new Uint16Array([65])), isAscii(new Uint16Array([65])));

                const detached = bytes.buffer;
                structuredClone(detached, { transfer: [detached] });
                for (const validate of [isUtf8, isAscii]) {
                    console.log(validate(view));
                    try { validate(detached); }
                    catch (error) { console.log(error.name, error.code); }
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\ntrue\ntrue false false false\ntrue true\ntrue true\n" +
            "true\nError ERR_INVALID_STATE\ntrue\nError ERR_INVALID_STATE\n", output);
    }

    [Theory, ModeData]
    public void BufferModule_Transcode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { transcode } from 'buffer';
                const out = transcode(Buffer.from('hello'), 'utf8', 'latin1');
                console.log(out.toString('latin1'));
                console.log(out.length);
                const bytes = new Uint8Array([255, 195, 169, 255]);
                const view = new Uint8Array(bytes.buffer, 1, 2);
                console.log(transcode(view, 'utf8', 'latin1').toString('hex'));
                structuredClone(bytes.buffer, { transfer: [bytes.buffer] });
                console.log(transcode(view, 'utf8', 'latin1').length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello\n5\ne9\n0\n", output);
    }

    [Theory, ModeData]
    public void BufferModule_ConstantsAndSlowBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { constants, kMaxLength, kStringMaxLength, INSPECT_MAX_BYTES, SlowBuffer } from 'buffer';
                console.log(constants.MAX_LENGTH > 0);
                console.log(constants.MAX_STRING_LENGTH > 0);
                console.log(kMaxLength === constants.MAX_LENGTH);
                console.log(kStringMaxLength === constants.MAX_STRING_LENGTH);
                console.log(INSPECT_MAX_BYTES);
                console.log(SlowBuffer(8).length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n50\n8\n", output);
    }

    #endregion

    #region Buffer statics + read/write matrix (#1161)

    [Theory, ModeData]
    public void Buffer_Of_And_PoolSize(ExecutionMode mode)
    {
        var source = """
            const b = Buffer.of(72, 105);
            console.log(b.toString());
            console.log(b.length);
            console.log(Buffer.poolSize);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hi\n2\n8192\n", output);
    }

    [Theory, ModeData]
    public void Buffer_VariableLengthReadWrite(ExecutionMode mode)
    {
        var source = """
            const b = Buffer.from([1, 2, 3, 4, 5, 6]);
            console.log(b.readUIntLE(0, 3));
            console.log(b.readUIntBE(0, 3));
            console.log(b.readIntLE(3, 3));
            const w = Buffer.alloc(6);
            w.writeUIntLE(197121, 0, 3);
            console.log(w.readUIntLE(0, 3));
            w.writeIntBE(-1000, 0, 4);
            console.log(w.readIntBE(0, 4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("197121\n66051\n394500\n197121\n-1000\n", output);
    }

    [Theory, ModeData]
    public void Buffer_Subarray(ExecutionMode mode)
    {
        var source = """
            const b = Buffer.from([10, 20, 30, 40, 50]);
            console.log(b.subarray(1, 3).toString('hex'));
            console.log(b.subarray(2).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("141e\n3\n", output);
    }

    [Theory, ModeData]
    public void Buffer_LowercaseUintAliases(ExecutionMode mode)
    {
        var source = """
            const b = Buffer.from([1, 0, 2, 0]);
            console.log(b.readUint8(0));
            console.log(b.readUint16LE(0));
            const w = Buffer.alloc(2);
            w.writeUint16LE(513, 0);
            console.log(w.readUint16LE(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n513\n", output);
    }

    [Theory, ModeData]
    public void Buffer_CopyBytesFrom(ExecutionMode mode)
    {
        var source = """
            const u16 = new Uint16Array([0x0102, 0x0304]);
            const b = Buffer.copyBytesFrom(u16);
            console.log(b.length);
            const sub = Buffer.copyBytesFrom(u16, 1);
            console.log(sub.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n2\n", output);
    }

    #endregion

    #region Blob / File (#1159)

    // Blob/File are interpreter-complete; the compiled $Blob/$File IL type is a
    // documented follow-up (see the compiled deferral test below), so the behavioral
    // tests are interpreter-only.

    [Theory, InterpretedOnlyData]
    public void Blob_SizeTypeTextSlice(ExecutionMode mode)
    {
        var source = """
            const b = new Blob(['Hello, ', 'world!'], { type: 'text/plain' });
            console.log(b.size);
            console.log(b.type);
            b.text().then((t: string) => console.log(t));
            b.slice(0, 5).text().then((t: string) => console.log(t));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("13\ntext/plain\nHello, world!\nHello\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Blob_ArrayBufferBytesStream(ExecutionMode mode)
    {
        var source = """
            async function main() {
              const b = new Blob(['ab', 'cd']);
              const ab = await b.arrayBuffer();
              console.log(ab.byteLength);
              const bytes = await b.bytes();
              console.log(bytes.length + ' ' + bytes[0]);
              let total = 0;
              let first = -1;
              for await (const chunk of b.stream()) {
                total += chunk.length;
                if (first < 0) first = chunk[0];
              }
              console.log(total + ' ' + first);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n4 97\n4 97\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void File_NameLastModified(ExecutionMode mode)
    {
        var source = """
            const f = new File(['data'], 'f.txt', { type: 'text/plain', lastModified: 12345 });
            console.log(f.name);
            console.log(f.lastModified);
            console.log(f.size);
            console.log(f.type);
            f.text().then((t: string) => console.log(t));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("f.txt\n12345\n4\ntext/plain\ndata\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Blob_BufferModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Blob, File, resolveObjectURL } from 'buffer';
                const b = new Blob(['hi']);
                console.log(b.size);
                const f = new File(['x'], 'a.txt');
                console.log(f.name);
                console.log(resolveObjectURL('blob:unknown'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\na.txt\nundefined\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Blob_CompiledThrowsClearDeferral(ExecutionMode mode)
    {
        // Compiled mode emits a clear, documented deferral error rather than the
        // misleading "undefined variable Blob".
        var source = """
            let msg = '';
            try {
                const b = new Blob(['x']);
            } catch (e) {
                msg = (e as Error).message;
            }
            console.log(msg.includes('not yet supported in compiled mode'));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion
}
