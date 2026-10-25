using System.Reflection;
using System.Reflection.Emit;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Reads the types a compiled type depends on, for architecture rules that must hold however a
/// reference is spelled in source.
/// </summary>
/// <remarks>
/// A dependency is collected from a type's base type and interfaces, its generic constraints, the
/// signatures and custom attributes of the type and its members, the locals of its method bodies,
/// and every type, method, and field those bodies reference. What compilation erases, such as an
/// inlined constant or a <c>nameof</c>, is invisible here and is left to source checks.
/// </remarks>
internal static class TypeReferences
{
    /// <summary>
    /// The binding flags that select every member a type declares itself.
    /// </summary>
    private const BindingFlags Declared =
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every IL opcode keyed by its encoded value, used to step over operands while scanning method bodies.
    /// </summary>
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToDictionary(static code => code.Value);

    /// <summary>
    /// Collects every type a type depends on.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The referenced types, with array, by-ref, pointer, and generic types expanded into their parts.</returns>
    internal static IEnumerable<Type> Of(Type type)
    {
        List<Type> direct = [.. type.GetInterfaces(), .. AttributeTypes(type.GetCustomAttributesData())];

        if (type.BaseType is { } baseType)
        {
            direct.Add(baseType);
        }

        if (type.IsGenericTypeDefinition)
        {
            direct.AddRange(type.GetGenericArguments().SelectMany(ConstraintsOf));
        }

        foreach (var field in type.GetFields(Declared))
        {
            direct.Add(field.FieldType);
            direct.AddRange(AttributeTypes(field.GetCustomAttributesData()));
        }

        foreach (var property in type.GetProperties(Declared))
        {
            direct.Add(property.PropertyType);
            direct.AddRange(AttributeTypes(property.GetCustomAttributesData()));
        }

        foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
        {
            direct.AddRange(AttributeTypes(method.GetCustomAttributesData()));

            foreach (var parameter in method.GetParameters())
            {
                direct.Add(parameter.ParameterType);
                direct.AddRange(AttributeTypes(parameter.GetCustomAttributesData()));
            }

            if (method is MethodInfo info)
            {
                direct.Add(info.ReturnType);

                if (info.IsGenericMethodDefinition)
                {
                    direct.AddRange(info.GetGenericArguments().SelectMany(ConstraintsOf));
                }
            }

            direct.AddRange(TypesUsedByBody(method));
        }

        return direct.SelectMany(Expand);
    }

    /// <summary>
    /// Gets the constraint types of a generic parameter.
    /// </summary>
    /// <param name="parameter">The generic parameter.</param>
    /// <returns>Its base type and interface constraints.</returns>
    private static Type[] ConstraintsOf(Type parameter)
    {
        return parameter.GetGenericParameterConstraints();
    }

    /// <summary>
    /// Gets the attribute types and the <see cref="Type"/> arguments of custom attributes.
    /// </summary>
    /// <param name="attributes">The attributes applied to a type or member.</param>
    /// <returns>The types they name.</returns>
    private static IEnumerable<Type> AttributeTypes(IEnumerable<CustomAttributeData> attributes)
    {
        return attributes.SelectMany(static attribute =>
            attribute
                .ConstructorArguments.Concat(attribute.NamedArguments.Select(static named => named.TypedValue))
                .Select(static argument => argument.Value)
                .OfType<Type>()
                .Prepend(attribute.AttributeType)
        );
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
}
