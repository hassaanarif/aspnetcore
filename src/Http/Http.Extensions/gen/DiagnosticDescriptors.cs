// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;

namespace Microsoft.AspNetCore.Http.Generators;

public static class DiagnosticDescriptors
{
    public static DiagnosticDescriptor UnableToResolveRoutePattern { get; } = new DiagnosticDescriptor(
        "RDG001",
        "Unable to resolve route pattern",
        "Unable to statically resolve route pattern for endpoint. Compile-time endpoint generation will skip this endpoint.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static DiagnosticDescriptor UnableToResolveMethod { get; } = new DiagnosticDescriptor(
        "RDG002",
        "Unable to resolve endpoint handler",
        "Unable to statically resolve endpoint handler method. Only method groups, lambda expressions or readonly fields/variables are allowed. Compile-time endpoint generation will skip this endpoint.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}
