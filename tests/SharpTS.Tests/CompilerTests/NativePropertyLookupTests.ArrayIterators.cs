using System.Reflection;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed partial class NativePropertyLookupTests
{
    public static IEnumerable<object[]> OwnedArrayIteratorCases()
    {
        (string Name,string Source,string Expected)[] cases =
        [
            ("alias-transfer-original","const f:any=structuredClone;const b:any=new ArrayBuffer(2);console.log(f(7,{transfer:[b]}),b.byteLength);","7 0\n"),
            ("direct-transfer","const b:any=new ArrayBuffer(2);const v:any=structuredClone(9,{transfer:[b]});console.log(v,b.byteLength);\n","9 0\n"),
            ("transfer-getter-once","const b:any=new ArrayBuffer(2);let reads=0;const options:any={get transfer(){reads++;return [b]}};console.log(structuredClone(7,options),b.byteLength,reads);\n","7 0 1\n"),
            ("live-array-iterator","const a:any=[1];const it:any=a.values();a.push(2);console.log(it.next().value,it.next().value,it.next().done);\n","1 2 true\n"),
            ("own-next-callable","const b:any=new ArrayBuffer(2);const it:any=[b].values();let calls=0;Object.defineProperty(it,\"next\",{value(){calls++;return calls===1?{value:b,done:false}:{done:true}}});const list:any={[Symbol.iterator](){return it}};console.log(structuredClone(7,{transfer:list}),b.byteLength,calls);\n","7 0 2\n"),
            ("own-next-undefined","const b:any=new ArrayBuffer(2);const it:any=[b].values();Object.defineProperty(it,\"next\",{value:undefined});const list:any={[Symbol.iterator](){return it}};try{structuredClone(7,{transfer:list})}catch(e){console.log(e instanceof TypeError,b.byteLength)}\n","true 2\n"),
            ("own-next-getterless","const b:any=new ArrayBuffer(2);const it:any=[b].values();Object.defineProperty(it,\"next\",{set(value:any){}});const list:any={[Symbol.iterator](){return it}};try{structuredClone(7,{transfer:list})}catch(e){console.log(e instanceof TypeError,b.byteLength)}\n","true 2\n"),
            ("own-next-getter-once","const b:any=new ArrayBuffer(2);const it:any=[b].values();let reads=0;Object.defineProperty(it,\"next\",{get(){reads++;return undefined}});const list:any={[Symbol.iterator](){return it}};try{structuredClone(7,{transfer:list})}catch(e){console.log(e instanceof TypeError,b.byteLength,reads)}\n","true 2 1\n"),
            ("inherited-next-callable","const b:any=new ArrayBuffer(2);const it:any=[b].values();let calls=0;let same=true;const ancestor:any={next(){same=same&&this===it;calls++;return calls===1?{value:b,done:false}:{done:true}}};Object.setPrototypeOf(it,ancestor);const list:any={[Symbol.iterator](){return it}};console.log(structuredClone(7,{transfer:list}),b.byteLength,calls,same);\n","7 0 2 true\n"),
            ("inherited-next-undefined","const b:any=new ArrayBuffer(2);const it:any=[b].values();Object.setPrototypeOf(it,{next:undefined});const list:any={[Symbol.iterator](){return it}};try{structuredClone(7,{transfer:list})}catch(e){console.log(e instanceof TypeError,b.byteLength)}\n","true 2\n"),
            ("iterator-getter-once","const b:any=new ArrayBuffer(2);let reads=0;let same=false;const list:any={get [Symbol.iterator](){reads++;return function(this:any){same=this===list;return [b].values()}}};console.log(structuredClone(7,{transfer:list}),b.byteLength,reads,same);\n","7 0 1 true\n"),
            ("invalid-transfer-keeps-owner","const b:any=new ArrayBuffer(2);try{structuredClone(7,{transfer:[b,{}]})}catch(e:any){console.log(e.name===\"DataCloneError\",b.byteLength)}\n","true 2\n")
        ];
        foreach (var row in cases)
            foreach (bool native in new[] { false, true })
                yield return [row.Name,row.Source,row.Expected,native];
    }

    [Theory]
    [MemberData(nameof(OwnedArrayIteratorCases))]
    public void ActualArrayIteratorOwnerPreservesGuestLookup(string name,string source,string expected,bool native)
    {
        _ = name;
        Assert.Equal(expected,Run(Compile(source,native,out _)));
    }

    [Fact]
    public void ArrayIteratorOwnershipRemainsExactToItsCompletedOutput()
    {
        var saved=Compile("const a:any=[1];console.log(a.values().next().value);",true,out var compiler);
        Type actual=saved.GetType("$ArrayIterator")!;
        var owner=Assert.Single(compiler.NativeProperties!.Inventory,entry =>
            entry.Owner.Module.ModuleVersionId==actual.Module.ModuleVersionId && entry.Owner.MetadataToken==actual.MetadataToken);
        Assert.NotEmpty(owner.Properties);
        Assert.Null(Lookup(saved).Invoke(null,[actual,"next"]));
        var selected=Assert.IsAssignableFrom<MethodInfo>(Lookup(saved).Invoke(null,[actual,"Current"]));
        Assert.Equal(actual.GetProperty("Current")!.GetMethod!.MetadataToken,selected.MetadataToken);
        ForeignIterator.Reads=0;
        var fields=saved.GetType("$Runtime")!.GetMethod("GetFieldsProperty")!;
        var rejected=Assert.Throws<TargetInvocationException>(() => fields.Invoke(null,[new ForeignIterator(),"Current"]));
        Assert.Contains(NativePropertyLookupCatalog.ForeignOwnerMessage,rejected.InnerException!.Message);
        Assert.Equal(0,ForeignIterator.Reads);
        Assert.Throws<TargetInvocationException>(() => Lookup(saved).Invoke(null,[typeof(List<object>.Enumerator),"Current"]));
    }

    public sealed class ForeignIterator : System.Collections.IEnumerator
    {
        public static int Reads;
        public object Current => ++Reads;
        public bool MoveNext() => false;
        public void Reset() { }
    }
}
