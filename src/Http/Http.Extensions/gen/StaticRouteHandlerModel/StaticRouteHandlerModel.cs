// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
namespace Microsoft.AspNetCore.Http.Generators.StaticRouteHandlerModel;

internal enum RequestParameterSource
{
    Query,
    Route,
    Header,
    Form,
    Service,
    BodyOrService,
}

internal record RequestParameter(string Name, string Type, RequestParameterSource Source, bool IsOptional, object? DefaultValue);
internal record EndpointRoute(string RoutePattern);
internal record EndpointResponse(string ResponseType, string WrappedResponseType, string ContentType, bool IsAwaitable, bool IsVoid, bool IsIResult);

internal record Endpoint((string, int) Location, IInvocationOperation Operation, WellKnownTypes WellKnownTypes, SemanticModel SemanticModel)
{
    public string HttpMethod { get; init; }
    public EndpointRoute Route { get; init; }
    public EndpointResponse Response { get; init; }
    public List<DiagnosticDescriptor> Diagnostics { get; init; } = new List<DiagnosticDescriptor>();
};
