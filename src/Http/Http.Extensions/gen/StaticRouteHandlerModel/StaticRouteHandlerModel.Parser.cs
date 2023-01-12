// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.AspNetCore.Http.Generators.StaticRouteHandlerModel;

internal static class StaticRouteHandlerModelParser
{
    private const int RoutePatternArgumentOrdinal = 1;
    private const int RouteHandlerArgumentOrdinal = 2;

    public static Endpoint WithEndpointRoute(this Endpoint endpoint)
    {
        if (!TryGetRouteHandlerPattern(endpoint.Operation, out var routeToken))
        {
            return endpoint with
            {
                Diagnostics = new(endpoint.Diagnostics)
                {
                    DiagnosticDescriptors.UnableToResolveRoutePattern
                }
            };
        }

        return endpoint with
        {
            Route = new EndpointRoute(routeToken.ValueText)
        };
    }

    public static Endpoint WithEndpointResponse(this Endpoint endpoint)
    {
        if (!TryGetRouteHandlerMethod(endpoint.Operation, out var method))
        {
            return endpoint with
            {
                Diagnostics = new(endpoint.Diagnostics)
                {
                    DiagnosticDescriptors.UnableToResolveMethod
                }
            };
        }

        return endpoint with
        {
            Response = new EndpointResponse(
                UnwrapResponseType(method, endpoint).ToString(),
                method.ReturnType.ToString(),
                GetContentType(method, endpoint),
                IsAwaitable(method, endpoint),
                IsVoid: method.ReturnsVoid,
                IsIResult(method, endpoint))
        };

        static ITypeSymbol UnwrapResponseType(IMethodSymbol method, Endpoint endpoint)
        {
            var returnType = method.ReturnType;
            var task = endpoint.WellKnownTypes.Get(WellKnownType.System_Threading_Tasks_Task);
            var taskOfT = endpoint.WellKnownTypes.Get(WellKnownType.System_Threading_Tasks_Task_T);
            var valueTask = endpoint.WellKnownTypes.Get(WellKnownType.System_Threading_Tasks_ValueTask);
            var valueTaskOfT = endpoint.WellKnownTypes.Get(WellKnownType.System_Threading_Tasks_ValueTask_T);
            if (returnType.OriginalDefinition.Equals(taskOfT, SymbolEqualityComparer.Default) || returnType.OriginalDefinition.Equals(valueTaskOfT, SymbolEqualityComparer.Default))
            {
                return ((INamedTypeSymbol)returnType).TypeArguments[0];
            }

            if (returnType.OriginalDefinition.Equals(task, SymbolEqualityComparer.Default) || returnType.OriginalDefinition.Equals(valueTask, SymbolEqualityComparer.Default))
            {
                return null;
            }

            return returnType;
        }

        static bool IsAwaitable(IMethodSymbol method, Endpoint endpoint)
        {
            var potentialGetAwaiters = endpoint.SemanticModel.LookupSymbols(0,
                container: method.ReturnType.OriginalDefinition,
                name: WellKnownMemberNames.GetAwaiter,
                includeReducedExtensionMethods: true);
            var getAwaiters = potentialGetAwaiters.OfType<IMethodSymbol>().Where(x => !x.Parameters.Any());
            return getAwaiters.Any(symbol => symbol.Name == WellKnownMemberNames.GetAwaiter && VerifyGetAwaiter(symbol));
        }

        static bool VerifyGetAwaiter(IMethodSymbol getAwaiter)
        {
            var returnType = getAwaiter.ReturnType;
            if (returnType == null)
            {
                return false;
            }

            // bool IsCompleted { get }
            if (!returnType.GetMembers().OfType<IPropertySymbol>().Any(p => p.Name == WellKnownMemberNames.IsCompleted && p.Type.SpecialType == SpecialType.System_Boolean && p.GetMethod != null))
            {
                return false;
            }

            var methods = returnType.GetMembers().OfType<IMethodSymbol>();

            // NOTE: (vladres) The current version of C# Spec, §7.7.7.3 'Runtime evaluation of await expressions', requires that
            // NOTE: the interface method INotifyCompletion.OnCompleted or ICriticalNotifyCompletion.UnsafeOnCompleted is invoked
            // NOTE: (rather than any OnCompleted method conforming to a certain pattern).
            // NOTE: Should this code be updated to match the spec?

            // void OnCompleted(Action)
            // Actions are delegates, so we'll just check for delegates.
            if (!methods.Any(x => x.Name == WellKnownMemberNames.OnCompleted && x.ReturnsVoid && x.Parameters.Length == 1 && x.Parameters.First().Type.TypeKind == TypeKind.Delegate))
            {
                return false;
            }

            // void GetResult() || T GetResult()
            return methods.Any(m => m.Name == WellKnownMemberNames.GetResult && !m.Parameters.Any());
        }

        static bool IsIResult(IMethodSymbol method, Endpoint endpoint)
        {
            var returnType = UnwrapResponseType(method, endpoint);
            var resultType = endpoint.WellKnownTypes.Get(WellKnownType.Microsoft_AspNetCore_Http_IResult);
            return WellKnownTypes.Implements(returnType, resultType) ||
                SymbolEqualityComparer.Default.Equals(returnType, resultType);
        }

        static string? GetContentType(IMethodSymbol method, Endpoint endpoint)
        {
            // `void` returning methods do not have a Content-Type.
            // We don't have a strategy for resolving a Content-Type
            // from an IResult. Typically, this would be done via an
            // IEndpointMetadataProvider so we don't need to set a
            // Content-Type here.
            var resultType = endpoint.WellKnownTypes.Get(WellKnownType.Microsoft_AspNetCore_Http_IResult);
            if (method.ReturnsVoid ||
                WellKnownTypes.Implements(method.ReturnType , resultType) ||
                SymbolEqualityComparer.Default.Equals(method.ReturnType, resultType))
            {
                return null;
            }
            return method.ReturnType.SpecialType is SpecialType.System_String ? "text/plain" : "application/json";
        }
    }

    public static Endpoint WithHttpMethod(this Endpoint endpoint)
    {
        if (endpoint.Operation.Syntax is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax
                {
                    Identifier: { ValueText: var method }
                }
            },
            ArgumentList: { Arguments: { Count: 2 } args }
        })
        {
            return endpoint with
            {
                HttpMethod = method
            };
        }

        return endpoint;
    }

    public static Endpoint WithEndpoint(IInvocationOperation operation, WellKnownTypes wellKnownTypes, SemanticModel semanticModel)
    {
        var filePath = operation.Syntax.SyntaxTree.FilePath;
        var span = operation.Syntax.SyntaxTree.GetLineSpan(operation.Syntax.Span);
        var lineNumber = span.EndLinePosition.Line + 1;

        return new Endpoint((filePath, lineNumber), operation, wellKnownTypes, semanticModel);
    }

    private static bool TryGetRouteHandlerPattern(IInvocationOperation invocation, out SyntaxToken token)
    {
        IArgumentOperation? argumentOperation = null;
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == RoutePatternArgumentOrdinal)
            {
                argumentOperation = argument;
            }
        }
        if (argumentOperation?.Syntax is not ArgumentSyntax routePatternArgumentSyntax ||
            routePatternArgumentSyntax.Expression is not LiteralExpressionSyntax routePatternArgumentLiteralSyntax)
        {
            token = default;
            return false;
        }
        token = routePatternArgumentLiteralSyntax.Token;
        return true;
    }

    private static bool TryGetRouteHandlerMethod(IInvocationOperation invocation, out IMethodSymbol method)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == RouteHandlerArgumentOrdinal)
            {
                method = ResolveMethodFromOperation(argument);
                return true;
            }
        }
        method = null;
        return false;
    }

    private static IMethodSymbol ResolveMethodFromOperation(IOperation operation) => operation switch
    {
        IArgumentOperation argument => ResolveMethodFromOperation(argument.Value),
        IConversionOperation conv => ResolveMethodFromOperation(conv.Operand),
        IDelegateCreationOperation del => ResolveMethodFromOperation(del.Target),
        IFieldReferenceOperation { Field.IsReadOnly: true } f when ResolveDeclarationOperation(f.Field, operation.SemanticModel) is IOperation op =>
            ResolveMethodFromOperation(op),
        IAnonymousFunctionOperation anon => anon.Symbol,
        ILocalFunctionOperation local => local.Symbol,
        IMethodReferenceOperation method => method.Method,
        IParenthesizedOperation parenthesized => ResolveMethodFromOperation(parenthesized.Operand),
        _ => null
    };

    private static IOperation ResolveDeclarationOperation(ISymbol symbol, SemanticModel semanticModel)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var syn = syntaxReference.GetSyntax();

            if (syn is VariableDeclaratorSyntax
            {
                Initializer:
                {
                    Value: var expr
                }
            })
            {
                // Use the correct semantic model based on the syntax tree
                var operation = semanticModel.GetOperation(expr);

                if (operation is not null)
                {
                    return operation;
                }
            }
        }

        return null;
    }
}
