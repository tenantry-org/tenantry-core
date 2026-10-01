# `TenantRejectedContext<TKey>` class

Namespace: `Tenantry.AspNetCore` · Package: `Tenantry.AspNetCore` · [API reference](README.md)

A request that `app.UseTenantry()` rejects, passed to [`TenantResolutionOptions<TKey>.OnRejected`](tenantry-aspnetcore-tenantresolutionoptions.md).

[`TenantRejectedContext<TKey>.Reason`](tenantry-aspnetcore-tenantrejectedcontext.md) is the actual reason, for your logs. With access validators, a request whose tenant does not exist gets the response of one whose tenant it may not use ([`TenantRejectedContext<TKey>.StatusCode`](tenantry-aspnetcore-tenantrejectedcontext.md) is the access-denied status), so a caller cannot find out which tenants exist: keep that in a response of your own.

```csharp
public sealed class TenantRejectedContext<TKey> where TKey : IEquatable<TKey>, IParsable<TKey>
```

## Type parameters

- `TKey`: The tenant identifier type.

## Properties

### `HttpContext`

The request.

```csharp
public HttpContext HttpContext { get; }
```

Value: `HttpContext`

### `Identifier`

The identifier the request sent, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) when it sent none. It is request input: do not repeat it in a response without encoding it.

```csharp
public string? Identifier { get; }
```

Value: `string`

### `IsHandled`

Whether [`TenantRejectedContext<TKey>.HandleResponse`](tenantry-aspnetcore-tenantrejectedcontext.md) was called.

```csharp
public bool IsHandled { get; }
```

Value: `bool`

### `Reason`

Why the request is rejected.

```csharp
public TenantRejectionReason Reason { get; }
```

Value: [`TenantRejectionReason`](tenantry-aspnetcore-tenantrejectionreason.md)

### `StatusCode`

The status code of Tenantry's response, from [`TenantResolutionOptions<TKey>`](tenantry-aspnetcore-tenantresolutionoptions.md). Change it to send another.

```csharp
public int StatusCode { get; set; }
```

Value: `int`

### `Tenant`

The tenant an access validator refused, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) for any other reason.

```csharp
public ITenantDescriptor<TKey>? Tenant { get; }
```

Value: [`ITenantDescriptor<TKey>`](tenantry-itenantdescriptor-1.md)

## Methods

### `HandleResponse()`

Tells Tenantry that the handler wrote the response, so it writes none.

```csharp
public void HandleResponse()
```
