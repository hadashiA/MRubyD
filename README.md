# MRubyD

MRubyD is a new [mruby](https://github.com/mruby/mruby) virtual machine implemented in pure C#. The name "MRubyD" stands for *mruby for [dotnet](https://dotnet.microsoft.com)* and pays homage to the well-known alternative implementation mruby/c. Designed with seamless integration in mind for C#-based game engines, and emphasize ruby level compatibility. MRubyD leverages the latest C# features for high performance and high extensibility.

> [!NOTE]
> This library is currently in preview

## Features

- **Implemented in C#**  
  Utilizes the robust capabilities of C# to ensure seamless integration with C#-based game engines.

- **High Performance**  
  Takes advantage of modern C# language features—such as managed pointers, `Span`, and the performance benefits of the .NET runtime’s GC and JIT compiler—to deliver superior speed and efficiency.

- **High compatibility with Ruby-level APIs** (Work in progress)
  It is intended for use in software with a certain amount of resources, such as games. For this reason, we are focusing on Ruby API compatibility.
  At release, all opcodes are implemented and pass the [syntax.rb](https://github.com/mruby/mruby/blob/master/test/t/syntax.rb) tests from the mruby repository.

- **Rich Library Integration & Extensibility**  
  Compared to the original C implementation, calling C#’s extensive libraries from Ruby is straightforward, making the VM highly extensible.

## Limitations (Preview Release)

This release is a preview version and comes with the following constraints:

- Built-in types and methods are still being implemented.
  - Please refer to [ruby test](https://github.com/hadashiA/MRubyD/tree/main/tests/MRubyD.Tests/ruby/test), etc., for currently supported methods.
  - We are working on supporting all methods that are built into mruby by default.
- `private` and `protected` visibitily is not yet implemented. (mruby got support for this in 3.4)
- This project provides only the VM implementation; it does not include a compiler. To compile mruby scripts, you need the native mruby-compiler.

### Most recent roadmap

- Implement builtin ruby libs
- Support Fiber
- All ruby code port to C# (for performance reason)
- Unity Integration
- [VitalRouter.MRuby](https://github.com/hadashiA/VitalRouter) for the new version.

## Installation

``` bash
otnet add package MRubyD
```

## Basic Usage

### Execute byte-code

```ruby
def fibonacci(n)
  return n if n <= 1 
  fibonacci(n - 1) + fibonacci(n - 2)
end

fibonacci 10
```

``` bash
$ mrbc -o fibonaci.mrbc fibonacci.rb
```

``` cs
using MRubyD;

// Read the .mrb byte-code.
var bytes = File.ReadAllBytes("fibonacci.mrb");

// initialize state
var state = MRubyState.Create();

// execute bytecoe
var result = state.Exec(bytes);

result.IsInteger    //=> true
result.IntegerValue //=> 55
```

This is a sample of executing bytecode.
See the [How to compile .mrb ](#compilation) section for information on how to convert Ruby source code to mruby bytecode.


### Handlding `MRubyValue`

Above `result` is `MRubyValue`. This represents a Ruby value.

``` cs
value.IsNil //=> true if integer
value.IsInteger //=> true if integer
value.IsFloat //=> true if integer
value.IsSymbol //=> true if Symbol
value.IsObject //=> true if any allocated object type

value.VType //=> get known ruby-type as C# enum.

value.IntegerValue //=> get as C# Int64
value.FloatValue //=> get as C# float
value.SymbolValue //=> get as `Symbol`

value.As<RString>() //=> get as object value

// pattern matching
if (vlaue.Object is RString str)
{
    // ...
}

swtich (value)
{
    case { IsInteger: true }:
        // ...
        break;
    case { Object: RString str }:
        // ...
        break;
}

var intValue = MRubyValue.From(100); // create int value
var floatValue = MRubyValue.From(1.234f); // create float value
var objValue = MRubyValue.From(str); // create allocated ruby object value
```

### Define ruby class/module/method by C# 

``` cs
// Create MRubyState object.
var state = MRubyState.Create();

// Define class
var classA = state.DefineClass(Intern("A"u8), c =>
{
    // Method definition that takes a required argument.
    c.DefineMethod(Intern("plus100"u8), (state, self) =>
    {
        var arg0 = state.GetArgAsIntegeger(0); // get first argument (index:0)
        return MRubyValue.From(arg0 + 100);
    });

    // Method definition that takes a block argument.
    c.DefineMethod(Intern("method2"), (state, self) =>
    {
        var arg0 = state.GetArg(0);
        var blockArg = state.GetBlockArg();
        if (!blockArg.IsNil)
        {
            // Execute `Proc#call`
            state.Send(blockArg, state.Intern("call"u8), arg0);
        }
    });
    
    // Other complex arguments...
    c.DefineMethod(Intern("method3"), (state, self) =>
    {
        var keywordArg = state.GetKeywordArg(state.Intern("foo"))
        Console.WriteLine($"foo: {keywordArg}");
        
        // argument type checking
        state.EnsureValue
        
        var restArguments = state.GetRestArguments();
        for (var i = 0; i < restArguments.Length; i++)
        {
            Console.WriteLine($"rest arg({i}: {restArguments[i]})");
        }
    });
    
    // class method
    c.DefineClassMethod(Intern("classmethod1"), (state, self) => 
    {
        var str = state.NewString($"hoge fuga");
        return MRubyValue.From(str);
    });
    
});

// Monkey patching
classA.DefineMethod(Intern("additional_method1"u8), (state, self) => { /* ... */ });

// Define module
var moduleA = state.DefineModule(Intern("ModuleA");)
state.DefineMethod(moduleA, Intern("additional_method2"u8), (state, self) => MRubyValue.From(123));

state.IncludeModule(classA, moduleA);
```

```ruby
a = A.new
a.plus100(123) #=> 223

a.method2(1) { |a| a } #=> 1

a.additionoa_method2 #=> 123

A.classmethod1 #=> "hoge fuga"
```

### Symbol/String

The string representation within mruby is utf8.
Therefore, to generate a ruby string from C#, [Utf8StringInterpolation](https://github.com/Cysharp/Utf8StringInterpolation) is used internally.


```cs
// Create string literal.
var str1 = state.NewString("HOGE HOGE"u8);

// Create string via interpolation
var x = 123;
var str2 = state.NewString($"x={x}");

// wrap MRubyValue..
var strValue = MRubyValue.From(str1);
```

There is a concept in mruby similar to String called `Symbol`.
Like String, it is created using utf8 strings, but internally it is a uint integer.
Symbols are usually used for method IDs and class IDs.

To create a symbol from C#, use `Intern`.

```cs
// symbol literal
var sym1 = state.Intern("sym"u8)

// symbol from string
var sym2 = state.ToSymbol(state.NewString("sym2"u8));
```

### How to compile .mrb 


MRubyD only includes the mruby virtual machine. Therefore it is necessary to convert it to .mrb bytecode before executing the .rb source.
Basically, you need the native compiler provided by the [mruby](https://github.com/mruby/mruby) project.

```bash
$ git clone git@github.com:mruby/mruby.git
$ cd mruby
$ rake
$ ./build/host/bin/mrubc
```

#### MRubyD.Compiler

To simplify compilation from C#, we also provide the MRubyD.Compiler package, which is a thin wrapper for the native compiler.

> [!NOTE]
> This MRubyD.Compiler package is a thin wrapper for the native binary. Currently, builds for linux (x64/arm64), macOS (x64/arm64), and windows (x64) are provided.

```cs
dotnet add package MRubyD.Compiler
```

```cs
using MRubyD.Compiler;

var source = """
def a
  1
end

a
"""u8;

var state = MRubyState.Create();
var compiler = MRubyCompiler.Create(state);

var irep = compiler.Compile(source);

state.Exec(irep); // => 1
```


## LICENSE

MIT

## Contact

[@hadahsiA](https://x.com/hadashiA)

