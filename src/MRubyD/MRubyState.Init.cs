using System;
using System.IO;
using System.Runtime.CompilerServices;
using MRubyD.Internals;
using MRubyD.StdLib;

namespace MRubyD;

public partial class MRubyState
{
    public static MRubyState Create()
    {
        var mrb = new MRubyState();
        mrb.InitClass();
        mrb.InitObject();
        mrb.InitKernel();
        mrb.InitSymbol();
        mrb.InitString();
        mrb.InitProc();
        mrb.InitException();
        mrb.InitNumeric();
        mrb.InitArray();
        mrb.InitHash();
        mrb.InitRange();
        // mrb.InitEnumerable();
        // mrb.InitComparable();
        mrb.InitMrbLib();
        return mrb;
    }

    public RClass BasicObjectClass { get; private set; } = default!;
    public RClass ObjectClass { get; private set; } = default!;
    public RClass ClassClass { get; private set; } = default!;
    public RClass ModuleClass { get; private set; } = default!;
    public RClass ProcClass { get; private set; } = default!;
    public RClass StringClass { get; private set; } = default!;
    public RClass ArrayClass { get; private set; } = default!;
    public RClass HashClass { get; private set; } = default!;
    public RClass RangeClass { get; private set; } = default!;
    public RClass FloatClass { get; private set; } = default!;
    public RClass IntegerClass { get; private set; } = default!;
    public RClass TrueClass { get; private set; } = default!;
    public RClass FalseClass { get; private set; } = default!;
    public RClass NilClass { get; private set; } = default!;
    public RClass SymbolClass { get; private set; } = default!;
    public RClass KernelModule { get; private set; } = default!;
    public RClass ExceptionClass { get; private set; } = default!;
    public RClass StandardErrorClass { get; private set; } = default!;

    public RObject TopSelf { get; private set; } = default!;
    public MRubyLongJumpException? Exception { get; private set; }

    readonly MRubyContext contextRoot;
    readonly MRubyContext context = new();
    readonly SymbolTable symbolTable = new();
    readonly VariableTable globalVariables = new();
    readonly MRubyValueEqualityComparer valueEqualityComparer;

    RiteParser? riteParser;

    // TODO:
    // readonly (RClass, MRubyMethod)[] methodCacheEntries = new (RClass, MRubyMethod)[MethodCacheSize];

    MRubyState()
    {
        contextRoot = context;
        valueEqualityComparer = new MRubyValueEqualityComparer(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(ReadOnlySpan<byte> name) => symbolTable.Intern(name);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(RString name) => symbolTable.Intern(name.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol InternLiteral(byte[] name) => symbolTable.InternLiteral(name);

    void InitClass()
    {
        BasicObjectClass = new RClass(ClassClass)
        {
            InstanceVType = MRubyVType.Undef,
            Super = default!, // sentinel. only for BasicObject
        };
        ObjectClass = new RClass(ClassClass)
        {
            InstanceVType = MRubyVType.Object,
            Super = BasicObjectClass,
        };
        ModuleClass = new RClass(ClassClass)
        {
            InstanceVType = MRubyVType.Module,
            Super = ObjectClass,
        };
        ClassClass = new RClass(default!)   // sentinel. only for ClassClass
        {
            InstanceVType = MRubyVType.Class,
            Super = ModuleClass,
        };

        BasicObjectClass.Class = ClassClass;
        ObjectClass.Class = ClassClass;
        ModuleClass.Class = ClassClass;
        ClassClass.Class = ClassClass;

        // Prepare singleton class
        PrepareSingletonClass(BasicObjectClass);
        PrepareSingletonClass(ObjectClass);
        PrepareSingletonClass(ModuleClass);
        PrepareSingletonClass(ClassClass);

        // name basic classes
        DefineConst(BasicObjectClass, Names.BasicObjectClass, MRubyValue.From(BasicObjectClass));
        DefineConst(ObjectClass, Names.ObjectClass, MRubyValue.From(ObjectClass));
        DefineConst(ObjectClass, Names.ModuleClass, MRubyValue.From(ModuleClass));
        DefineConst(ObjectClass, Names.ClassClass, MRubyValue.From(ClassClass));

        BasicObjectClass.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(NewString("BasicObject"u8)));
        ObjectClass.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(NewString("Object"u8)));
        ModuleClass.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(NewString("Module"u8)));
        ClassClass.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(NewString("Class"u8)));

        DefineMethod(BasicObjectClass, Names.Initialize, MRubyMethod.Nop);
        DefineMethod(BasicObjectClass, Names.OpNot, BasicObjectMembers.Not);
        DefineMethod(BasicObjectClass, Names.OpEq, BasicObjectMembers.OpEq);
        DefineMethod(BasicObjectClass, Intern("__id__"u8), BasicObjectMembers.Id);
        DefineMethod(BasicObjectClass, Intern("__send__"u8), BasicObjectMembers.Send);
        DefineMethod(BasicObjectClass, Names.QEqual, BasicObjectMembers.OpEq);
        DefineMethod(BasicObjectClass, Names.InstanceEval, BasicObjectMembers.InstanceEval);
        DefineMethod(BasicObjectClass, Names.SingletonMethodAdded, MRubyMethod.Nop);
        DefineMethod(BasicObjectClass, Names.MethodMissing, BasicObjectMembers.MethodMissing);

        DefineSingletonMethod(ClassClass, Names.New, ClassMembers.NewClass);
        DefineMethod(ClassClass, Names.New, ClassMembers.New);
        DefineMethod(ClassClass, Names.Initialize, ClassMembers.Initialize);
        DefineMethod(ClassClass, Intern("superclass"u8), ClassMembers.Superclass);
        DefineMethod(ClassClass, Intern("inherited"u8), MRubyMethod.Nop);

        DefineMethod(ModuleClass, Intern("extend_object"u8), ModuleMembers.ExtendObject);
        DefineMethod(ModuleClass, Intern("extended"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("prepended"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("prepend_features"u8), ModuleMembers.PrependFeatures);
        DefineMethod(ModuleClass, Intern("include?"u8), ModuleMembers.QInclude);
        DefineMethod(ModuleClass, Intern("append_features"u8), ModuleMembers.AppendFeatures);
        DefineMethod(ModuleClass, Intern("class_eval"u8), ModuleMembers.ClassEval);
        DefineMethod(ModuleClass, Intern("module_eval"u8), ModuleMembers.ClassEval);
        DefineMethod(ModuleClass, Intern("included"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Names.Initialize, ModuleMembers.Initialize);
        DefineMethod(ModuleClass, Intern("module_function"u8), ModuleMembers.ModuleFunction);
        DefineMethod(ModuleClass, Intern("private"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("protected"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("public"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("attr_reader"u8), ModuleMembers.AttrReader);
        DefineMethod(ModuleClass, Intern("attr_writer"u8), ModuleMembers.AttrWriter);
        DefineMethod(ModuleClass, Intern("attr_accessor"u8), ModuleMembers.AttrAccessor);
        DefineMethod(ModuleClass, Names.ToS, ModuleMembers.ToS);
        DefineMethod(ModuleClass, Names.Inspect, ModuleMembers.ToS);
        DefineMethod(ModuleClass, Intern("alias_method"u8), ModuleMembers.AliasMethod);
        DefineMethod(ModuleClass, Intern("ancestors"u8), ModuleMembers.Ancestors);
        DefineMethod(ModuleClass, Intern("undef_method"u8), ModuleMembers.UndefMethod);
        DefineMethod(ModuleClass, Intern("const_defined?"u8), ModuleMembers.ConstDefined);
        DefineMethod(ModuleClass, Intern("const_get"u8), ModuleMembers.ConstGet);
        DefineMethod(ModuleClass, Intern("const_set"u8), ModuleMembers.ConstSet);
        DefineMethod(ModuleClass, Intern("remove_const"u8), ModuleMembers.RemoveConst);
        DefineMethod(ModuleClass, Intern("const_missing"u8), ModuleMembers.ConstMissing);
        DefineMethod(ModuleClass, Intern("method_defined?"u8), ModuleMembers.MethodDefined);
        DefineMethod(ModuleClass, Intern("define_method"u8), ModuleMembers.DefineMethod);
        DefineMethod(ModuleClass, Names.OpEqq, ModuleMembers.Eqq);
        DefineMethod(ModuleClass, Names.Dup, ModuleMembers.Dup);
        DefineMethod(ModuleClass, Names.MethodAdded, MRubyMethod.Nop);

        UndefMethod(ClassClass, Intern("append_features"u8));
        UndefMethod(ClassClass, Intern("prepend_features"u8));
        UndefMethod(ClassClass, Intern("extend_object"u8));
        UndefMethod(ClassClass, Intern("module_function"u8));

        TopSelf = new RObject(MRubyVType.Object, ObjectClass);
        // DefineSingletonMethod(TopSelf, Names.Inspect, MainMembers.Inspect);
        // DefineSingletonMethod(TopSelf, Names.ToS, MainMembers.ToS);
        // DefineSingletonMethod(TopSelf, Intern("define_method"u8), MainMembers.DefineMethod);
    }

    void InitObject()
    {
        NilClass = DefineClass(Intern("NilClass"u8), ObjectClass, MRubyVType.False);
        UndefClassMethod(NilClass, Names.New);
        DefineMethod(NilClass, Names.OpAnd, FalseClassMembers.And);
        DefineMethod(NilClass, Names.OpOr, FalseClassMembers.Or);
        DefineMethod(NilClass, Names.OpXor, FalseClassMembers.Xor);
        DefineMethod(NilClass, Names.QNil, MRubyMethod.True);
        DefineMethod(NilClass, Names.ToS, NilClassMembers.Tos);
        DefineMethod(NilClass, Names.Inspect, NilClassMembers.Inspect);

        TrueClass = DefineClass(Intern("TrueClass"u8), ObjectClass, MRubyVType.True);
        UndefClassMethod(TrueClass, Names.New);
        DefineMethod(TrueClass, Names.OpAnd, TrueClassMembers.And);
        DefineMethod(TrueClass, Names.OpOr, TrueClassMembers.Or);
        DefineMethod(TrueClass, Names.OpXor, TrueClassMembers.Xor);
        DefineMethod(TrueClass, Names.ToS, TrueClassMembers.ToS);
        DefineMethod(TrueClass, Names.Inspect, TrueClassMembers.ToS);

        FalseClass = DefineClass(Intern("FalseClass"u8), ObjectClass, MRubyVType.False);
        UndefClassMethod(FalseClass, Names.New);
        DefineMethod(FalseClass, Names.OpAnd, FalseClassMembers.And);
        DefineMethod(FalseClass, Names.OpOr, FalseClassMembers.Or);
        DefineMethod(FalseClass, Names.OpXor, FalseClassMembers.Xor);
        DefineMethod(FalseClass, Names.ToS, FalseClassMembers.ToS);
        DefineMethod(FalseClass, Names.Inspect, FalseClassMembers.ToS);
    }

    void InitKernel()
    {
        KernelModule = DefineModule(Intern("Kernel"u8), ObjectClass);
        DefineClassMethod(KernelModule, Names.Raise, KernelMembers.Raise);

        DefineMethod(KernelModule, Names.OpEqq, KernelMembers.OpEqq);
        DefineMethod(KernelModule, Names.OpCmp, KernelMembers.Cmp);
        DefineMethod(KernelModule, Names.QBlockGiven, KernelMembers.BlockGiven);
        DefineMethod(KernelModule, Names.Clone, KernelMembers.Clone);
        DefineMethod(KernelModule, Names.Dup, KernelMembers.Dup);
        DefineMethod(KernelModule, Names.Inspect, KernelMembers.Inspect);
        DefineMethod(KernelModule, Names.InitializeCopy, KernelMembers.InitializeCopy);
        DefineMethod(KernelModule, Names.Raise, KernelMembers.Raise);
        DefineMethod(KernelModule, Names.Class, KernelMembers.Class);
        DefineMethod(KernelModule, Names.QEql, KernelMembers.Eql);
        DefineMethod(KernelModule, Names.QNil, MRubyMethod.False);
        DefineMethod(KernelModule, Intern("freeze"u8), KernelMembers.Freeze);
        DefineMethod(KernelModule, Intern("frozen?"u8), KernelMembers.Frozen);
        DefineMethod(KernelModule, Names.Hash, KernelMembers.Hash);
        DefineMethod(KernelModule, Intern("instance_of?"u8), KernelMembers.InstanceOf);
        DefineMethod(KernelModule, Names.QIsA, KernelMembers.KindOf);
        DefineMethod(KernelModule, Names.QKindOf, KernelMembers.KindOf);
        DefineMethod(KernelModule, Intern("iterator?"u8), KernelMembers.BlockGiven);
        DefineMethod(KernelModule, Intern("kind_of?"u8), KernelMembers.KindOf);
        DefineMethod(KernelModule, Names.Nil, MRubyMethod.Nop);
        DefineMethod(KernelModule, Intern("object_id"u8), KernelMembers.ObjectId);
        DefineMethod(KernelModule, Intern("p"u8), KernelMembers.P);
        DefineMethod(KernelModule, Intern("print"u8), KernelMembers.Print);
        DefineMethod(KernelModule, Intern("remove_instance_variable"u8), KernelMembers.RemoveInstanceVariable);
        DefineMethod(KernelModule, Names.QRespondTo, KernelMembers.RespondTo);
        DefineMethod(KernelModule, Names.QRespondToMissing, MRubyMethod.False);
        DefineMethod(KernelModule, Names.ToS, KernelMembers.ToS);
        DefineMethod(KernelModule, Intern("lambda"u8), KernelMembers.Lambda);
        DefineMethod(KernelModule, Intern("__case_eqq"u8), KernelMembers.CaseEqq);

        IncludeModule(ObjectClass, KernelModule);
    }

    void InitSymbol()
    {
        SymbolClass = DefineClass(Intern("Symbol"u8), ObjectClass, MRubyVType.Symbol);
        UndefClassMethod(SymbolClass, Names.New);

        DefineMethod(SymbolClass, Names.ToS, SymbolMembers.ToS);
        DefineMethod(SymbolClass, Names.Name, SymbolMembers.Name);
        DefineMethod(SymbolClass, Names.ToSym, MRubyMethod.Identity);
        DefineMethod(SymbolClass, Names.Inspect, SymbolMembers.Inspect);
        DefineMethod(SymbolClass, Names.OpCmp, SymbolMembers.Cmp);
        DefineMethod(SymbolClass, Names.OpEq, KernelMembers.Eql);
    }

    void InitProc()
    {
        ProcClass = DefineClass(Intern("Proc"u8), ObjectClass, MRubyVType.Proc);
        DefineClassMethod(ProcClass, Names.New, ProcMembers.New);
        DefineMethod(ProcClass, Intern("arity"u8), MRubyMethod.Nop);
        DefineMethod(ProcClass, Names.OpEq, ProcMembers.Eql);
        DefineMethod(ProcClass, Names.QEql, ProcMembers.Eql);

        // NOTE: Why implement Proc#call in byte code?
        // The arguments at the time of `call` method call need to be copied to Proc execution,
        // but bytecode does not need such copying
        var callProc = new RProc(
            new Irep
            {
                RegisterVariableCount = 2,
                Sequence = [(byte)OpCode.Call],
            },
            0,
            ProcClass)
        {
            Upper = null,
            Scope = null
        };
        callProc.SetFlag(MRubyObjectFlags.ProcStrict);
        callProc.SetFlag(MRubyObjectFlags.ProcScope);
        callProc.SetFlag(MRubyObjectFlags.Frozen);

        var callMethod = new MRubyMethod(callProc);
        DefineMethod(ProcClass, Names.Call, callMethod);
        DefineMethod(ProcClass, Names.OpAref, callMethod);
    }

    void InitException()
    {
        ExceptionClass = DefineClass(Intern("Exception"u8), ObjectClass, MRubyVType.Exception);
        DefineSingletonMethod(ExceptionClass, Names.Exception, ExceptionMembers.New);
        DefineMethod(ExceptionClass, Names.Exception, ExceptionMembers.Exception);
        DefineMethod(ExceptionClass, Names.Initialize, ExceptionMembers.Initialize);
        DefineMethod(ExceptionClass, Names.ToS, ExceptionMembers.ToS);
        DefineMethod(ExceptionClass, Intern("message"u8), ExceptionMembers.ToS);
        DefineMethod(ExceptionClass, Names.Inspect, ExceptionMembers.Inspect);
        DefineMethod(ExceptionClass, Intern("backtrace"u8), ExceptionMembers.Backtrace);

        StandardErrorClass = DefineClass(Intern("StandardError"u8), ExceptionClass);
        DefineClass(Names.RuntimeError, StandardErrorClass);
        DefineClass(Names.TypeError, StandardErrorClass);
        DefineClass(Names.ZeroDivisionError, StandardErrorClass);
        DefineClass(Names.ArgumentError, StandardErrorClass);
        DefineClass(Names.IndexError, StandardErrorClass);
        DefineClass(Names.RangeError, StandardErrorClass);
        DefineClass(Names.FrozenError, StandardErrorClass);
        DefineClass(Names.NotImplementedError, StandardErrorClass);
        DefineClass(Names.LocalJumpError, StandardErrorClass);
    }

    void InitNumeric()
    {
        var numericClass = DefineClass(Intern("Numeric"u8), ObjectClass);
        DefineMethod(numericClass, Intern("finite?"u8), MRubyMethod.True);
        DefineMethod(numericClass, Intern("infinite?"u8), MRubyMethod.False);
        DefineMethod(numericClass, Names.QEql, NumericMembers.Eql);

        IntegerClass = DefineClass(Intern("Integer"u8), numericClass, MRubyVType.Integer);
        UndefClassMethod(IntegerClass, Names.New);
        // DefineMethod(IntegerClass);
        DefineMethod(IntegerClass, Names.ToS, IntegerMembers.ToS);
        DefineMethod(IntegerClass, Names.Inspect, IntegerMembers.ToS);
        DefineMethod(IntegerClass, Names.OpMod, IntegerMembers.Mod);
        DefineMethod(IntegerClass, Names.OpPlus, IntegerMembers.OpPlus);
        DefineMethod(IntegerClass, Names.OpMinus, IntegerMembers.OpMinus);
        DefineMethod(IntegerClass, Intern("abs"u8), IntegerMembers.Abs);

        FloatClass = DefineClass(Intern("Float"u8), numericClass, MRubyVType.Float);
        UndefClassMethod(FloatClass, Names.New);
        DefineMethod(FloatClass, Names.ToI, FloatMembers.ToI);
        DefineMethod(FloatClass, Names.OpMod, FloatMembers.Mod);
    }

    void InitString()
    {
        StringClass = DefineClass(Intern("String"u8), ObjectClass, MRubyVType.String);
        DefineMethod(StringClass, Names.OpEq, StringMembers.OpEq);
        DefineMethod(StringClass, Names.QEql, StringMembers.OpEq);
        DefineMethod(StringClass, Names.Inspect, StringMembers.Inspect);
        DefineMethod(StringClass, Names.ToSym, StringMembers.ToSym);
        DefineMethod(StringClass, Names.ToI, StringMembers.ToI);
    }

    void InitArray()
    {
        ArrayClass = DefineClass(Intern("Array"u8), ObjectClass, MRubyVType.Array);

        DefineMethod(ArrayClass, Names.OpEq, ArrayMembers.OpEq);
        DefineMethod(ArrayClass, Names.QEql, ArrayMembers.Eql);
        DefineMethod(ArrayClass, Names.OpLShift, ArrayMembers.Push);
        DefineMethod(ArrayClass, Names.OpAdd, ArrayMembers.OpAdd);
        DefineMethod(ArrayClass, Names.Initialize, ArrayMembers.Initialize);
        DefineMethod(ArrayClass, Intern("push"u8), ArrayMembers.Push);
        DefineMethod(ArrayClass, Intern("size"u8), ArrayMembers.Size);
        DefineMethod(ArrayClass, Intern("length"u8), ArrayMembers.Size);
        DefineMethod(ArrayClass, Intern("empty?"u8), ArrayMembers.Empty);
        DefineMethod(ArrayClass, Intern("first"u8), ArrayMembers.First);
        DefineMethod(ArrayClass, Intern("last"u8), ArrayMembers.Last);
        DefineMethod(ArrayClass, Intern("reverse!"u8), ArrayMembers.ReverseBang);
        DefineMethod(ArrayClass, Names.ToS, ArrayMembers.ToS);
        DefineMethod(ArrayClass, Names.Inspect, ArrayMembers.ToS);
        DefineMethod(ArrayClass, Intern("__svalue"u8), ArrayMembers.SValue);
    }

    void InitHash()
    {
        HashClass = DefineClass(Intern("Hash"u8), ObjectClass, MRubyVType.Hash);
        DefineMethod(HashClass, Names.ToS, HashMembers.ToS);
        DefineMethod(HashClass, Names.Inspect, HashMembers.ToS);
        DefineMethod(HashClass, Names.OpEq, HashMembers.OpEq);
        DefineMethod(HashClass, Names.QEql, HashMembers.Eql);
    }

    void InitRange()
    {
        RangeClass = DefineClass(Intern("Range"u8), ObjectClass, MRubyVType.Range);
        DefineMethod(RangeClass, Intern("begin"u8), RangeMembers.Begin);
        DefineMethod(RangeClass, Intern("end"u8), RangeMembers.End);
        DefineMethod(RangeClass, Intern("exclude_end?"u8), RangeMembers.ExcludeEnd);
        DefineMethod(RangeClass, Names.OpEq, RangeMembers.OpEq);
        DefineMethod(RangeClass, Names.OpEqq, RangeMembers.IsInclude);
        DefineMethod(RangeClass, Names.QInclude, RangeMembers.IsInclude);
        DefineMethod(RangeClass, Intern("member?"u8), RangeMembers.IsInclude);
        DefineMethod(RangeClass, Names.ToS, RangeMembers.ToS);
        DefineMethod(RangeClass, Names.Inspect, RangeMembers.Inspect);
    }

    // void InitComparable()
    // {
    //     var comparableModule = DefineModule(Intern("Comparable"u8), ObjectClass);
    //     // DefineMethod(comparableModule, Names.OpEq);
    // }

    void InitMrbLib()
    {
        riteParser ??= new RiteParser(this);
        var executingDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        foreach (var path in Directory.EnumerateFiles(executingDir!, "*.mrb", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(path);
            var irep = riteParser.Parse(bytes);
            Exec(irep);
        }
    }

    bool TrySetClassPathLink(RClass outer, RClass c, Symbol name)
    {
        if (c.InstanceVariables.TryGet(Names.OuterKey, out _)) return false;

        c.InstanceVariables.Set(Names.OuterKey, MRubyValue.From(outer));
        outer.InstanceVariables.Set(name, MRubyValue.From(c));

        if (!c.InstanceVariables.TryGet(Names.ClassNameKey, out _))
        {
            c.InstanceVariables.Set(Names.ClassNameKey, MRubyValue.From(name));
        }
        return true;
    }
}
