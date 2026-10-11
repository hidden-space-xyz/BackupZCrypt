using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Test.Tools;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Enforces the dependency rules between the feature folders of the Application layer, so a feature
/// cannot quietly start depending on one that depends on it.
/// </summary>
/// <remarks>
/// <para>
/// The features form a directed acyclic graph: <c>Common</c> depends on no feature, the chunked
/// <c>Engine</c> depends only on <c>Common</c> and <c>Manifest</c>, and the use-case features sit on
/// top.
/// </para>
/// <para>
/// Each feature is checked twice, because neither view sees every reference. The compiled view reads
/// every type a feature's members and method bodies use, which catches a reference however it was
/// spelled, including a name qualified only relative to the shared parent namespace. The source
/// view reads every mention of another feature's namespace, which also catches what compilation
/// erases: a documentation <c>cref</c>, an unused <c>using</c>, or an inlined constant.
/// </para>
/// </remarks>
public sealed partial class FeatureDependencyTests
{
    /// <summary>
    /// The project whose feature folders are checked.
    /// </summary>
    private const string ApplicationProject = "BackupZCrypt.Application";

    /// <summary>
    /// The namespace every Application feature namespace sits directly under.
    /// </summary>
    private const string ApplicationNamespace = "BackupZCrypt.Application";

    /// <summary>
    /// The features each Application feature is allowed to reference: the single source of truth for
    /// these rules, which AGENTS.md points to.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedFeatureReferences = new(
        StringComparer.Ordinal
    )
    {
        ["Common"] = [],
        ["Manifest"] = ["Common"],
        ["Engine"] = ["Common", "Manifest"],
        ["Passwords"] = ["Common"],
        ["Settings"] = ["Common"],
        ["Benchmark"] = ["Common", "Engine"],
        ["Backup"] = ["Common", "Engine", "Manifest", "Passwords"],
    };

    /// <summary>
    /// Every IL opcode keyed by its encoded value, used to step over operands while scanning method bodies.
    /// </summary>
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToDictionary(static code => code.Value);

    /// <summary>
    /// Gets the features under the dependency rules, as the theory data that runs the checks once per
    /// feature.
    /// </summary>
    public static TheoryData<string> Features => new(AllowedFeatureReferences.Keys);

    [Theory]
    [MemberData(nameof(Features))]
    internal void CompiledFeatureReferences_StayWithinTheDocumentedFeatureRules(string feature)
    {
        var allowed = AllowedOrSelf(feature);

        var violations = ApplicationTypes()
            .Where(type => FeatureOf(type) == feature)
            .SelectMany(type =>
                ReferencedTypes(type)
                    .Select(FeatureOf)
                    .OfType<string>()
                    .Where(referenced => !allowed.Contains(referenced))
                    .Select(referenced => $"{type.FullName} -> {referenced}")
            )
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(Features))]
    internal void SourceFeatureReferences_StayWithinTheDocumentedFeatureRules(string feature)
    {
        var allowed = AllowedOrSelf(feature);

        var violations = SourceFilesOf(feature)
            .SelectMany(file =>
                ReferencedFeatures(File.ReadAllText(file))
                    .Where(referenced => !allowed.Contains(referenced))
                    .Select(referenced => $"{Path.GetFileName(file)} -> {referenced}")
            )
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    internal void EveryApplicationType_BelongsToAFeatureWithDocumentedRules()
    {
        var undocumented = ApplicationTypes()
            .Where(static type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(static type => type.Namespace?.StartsWith(ApplicationNamespace, StringComparison.Ordinal) is true)
            .Where(static type => FeatureOf(type) is not { } feature || !AllowedFeatureReferences.ContainsKey(feature))
            .Select(static type => type.FullName)
            .ToList();

        Assert.Empty(undocumented);
    }

    [Fact]
    internal void EveryApplicationSourceFile_SitsInAFeatureFolderWithDocumentedRules()
    {
        var projectRoot = Path.Join(OnDiskFormatFixtures.RepositoryRoot, ApplicationProject);

        var misplaced = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(projectRoot, file))
            .Where(static path => !path.StartsWith("bin", StringComparison.Ordinal))
            .Where(static path => !path.StartsWith("obj", StringComparison.Ordinal))
            .Where(static path =>
                path.Split(Path.DirectorySeparatorChar) is not [var folder, _, ..]
                || !AllowedFeatureReferences.ContainsKey(folder)
            )
            .ToList();

        Assert.Empty(misplaced);
    }

    /// <summary>
    /// Builds the set of features a feature may reference, including itself.
    /// </summary>
    /// <param name="feature">The feature being checked.</param>
    /// <returns>The allowed feature names.</returns>
    private static HashSet<string> AllowedOrSelf(string feature)
    {
        return AllowedFeatureReferences[feature].Append(feature).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Lists every type compiled into the Application assembly, nested and compiler-generated ones
    /// included.
    /// </summary>
    /// <returns>The types.</returns>
    private static Type[] ApplicationTypes()
    {
        return typeof(Result).Assembly.GetTypes();
    }

    /// <summary>
    /// Gets the Application feature a type belongs to, from its namespace.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <returns>The feature name, or <see langword="null"/> for a type outside every feature namespace.</returns>
    private static string? FeatureOf(Type type)
    {
        var prefix = ApplicationNamespace + ".";

        if (type.Namespace is not { } ns || !ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = ns[prefix.Length..];
        var separator = rest.IndexOf('.', StringComparison.Ordinal);

        return separator < 0 ? rest : rest[..separator];
    }

    /// <summary>
    /// Collects every type a type depends on through its base types, its members' signatures, the
    /// locals of its method bodies, and the types, methods, and fields those bodies reference.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The referenced types, with array, by-ref, and generic types expanded into their parts.</returns>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags declared =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        List<Type> direct = [.. type.GetInterfaces()];

        if (type.BaseType is { } baseType)
        {
            direct.Add(baseType);
        }

        direct.AddRange(type.GetFields(declared).Select(static field => field.FieldType));

        foreach (var method in type.GetMethods(declared).Cast<MethodBase>().Concat(type.GetConstructors(declared)))
        {
            direct.AddRange(method.GetParameters().Select(static parameter => parameter.ParameterType));

            if (method is MethodInfo info)
            {
                direct.Add(info.ReturnType);
            }

            direct.AddRange(TypesUsedByBody(method));
        }

        return direct.SelectMany(Expand);
    }

    /// <summary>
    /// Collects the local variable types of a method body and the declaring and signature types of
    /// every type, method, and field its IL references.
    /// </summary>
    /// <param name="method">The method whose body is scanned.</param>
    /// <returns>The types the body uses.</returns>
    private static IEnumerable<Type> TypesUsedByBody(MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body?.GetILAsByteArray() is not { } il)
        {
            yield break;
        }

        foreach (var local in body.LocalVariables)
        {
            yield return local.LocalType;
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring
            ? declaring.GetGenericArguments()
            : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } ? method.GetGenericArguments() : null;

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[position++]));
            }

            var operandType = OpCodesByValue[value].OperandType;

            if (operandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                var token = BitConverter.ToInt32(il, position);

                foreach (var used in TypesOfMember(method.Module.ResolveMember(token, typeArguments, methodArguments)))
                {
                    yield return used;
                }
            }

            position += OperandSize(operandType, il, position);
        }
    }

    /// <summary>
    /// Gets the types a member referenced from IL brings with it.
    /// </summary>
    /// <param name="member">The resolved member.</param>
    /// <returns>Its declaring type and the types of its signature.</returns>
    private static IEnumerable<Type> TypesOfMember(MemberInfo? member)
    {
        switch (member)
        {
            case Type referenced:
                yield return referenced;
                break;
            case FieldInfo field:
                yield return field.DeclaringType!;
                yield return field.FieldType;
                break;
            case MethodBase called:
                if (called.DeclaringType is { } owner)
                {
                    yield return owner;
                }

                foreach (var parameter in called.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                if (called is MethodInfo info)
                {
                    yield return info.ReturnType;

                    foreach (var argument in info.IsGenericMethod ? info.GetGenericArguments() : [])
                    {
                        yield return argument;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Gets the length in bytes of an IL instruction's operand.
    /// </summary>
    /// <param name="operandType">The operand kind of the instruction.</param>
    /// <param name="il">The method body's IL.</param>
    /// <param name="position">The offset of the operand.</param>
    /// <returns>The operand length.</returns>
    private static int OperandSize(OperandType operandType, byte[] il, int position)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
            _ => 4,
        };
    }

    /// <summary>
    /// Expands a type into itself and the types it is composed of: the element type of an array,
    /// pointer, or by-ref type, and the definition and arguments of a generic type.
    /// </summary>
    /// <param name="type">The type to expand.</param>
    /// <returns>The type and its parts.</returns>
    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            return Expand(type.GetElementType()!);
        }

        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericArguments().SelectMany(Expand).Prepend(type.GetGenericTypeDefinition())
            : [type];
    }

    /// <summary>
    /// Lists the C# source files of one feature folder.
    /// </summary>
    /// <param name="feature">The feature folder name.</param>
    /// <returns>The full paths of its source files.</returns>
    private static IEnumerable<string> SourceFilesOf(string feature)
    {
        var folder = Path.Join(OnDiskFormatFixtures.RepositoryRoot, ApplicationProject, feature);

        Assert.True(Directory.Exists(folder), $"Could not find {folder}.");
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Extracts the Application features a source file mentions by namespace.
    /// </summary>
    /// <param name="source">The file's text.</param>
    /// <returns>The distinct feature names referenced.</returns>
    private static IEnumerable<string> ReferencedFeatures(string source)
    {
        return FeatureNamespace()
            .Matches(source)
            .Select(static match => match.Groups["feature"].Value)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Matches a mention of an Application feature namespace, fully qualified or relative to the
    /// <c>BackupZCrypt</c> root, and captures the feature name.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"(?<![\w.])(?:BackupZCrypt\.)?Application\.(?<feature>[A-Za-z]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex FeatureNamespace();
}
