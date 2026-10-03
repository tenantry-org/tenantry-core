# `TenantContextGuard` class

Namespace: `Tenantry.EfCore` · Package: `Tenantry.EfCore` · [API reference](README.md)

An interceptor that checks a context before it opens a connection, before every command it runs and before `SaveChanges`, so a context that would reach the wrong tenant's data throws instead. Derive from it to fail closed on a condition of your own, such as a context whose schema is not the current tenant's.

Add it to a context's options with `AddInterceptors`, or from an [`ITenantDbContextOptionsContributor`](tenantry-efcore-itenantdbcontextoptionscontributor.md). EF Core raises no event for a connection that is already open, so a context opened under one tenant and then used under another is caught at its next command. `SaveChanges` is checked before it starts, because EF Core wraps an exception thrown while a save runs its commands in a `DbUpdateException`. A command EF Core runs without a context (a HiLo sequence fetch) is checked against the context that opened its connection.

Throw [`TenantNotResolvedException`](tenantry-tenantnotresolvedexception.md) when the context needs a tenant and none is current, and [`TenantIsolationViolationException`](tenantry-efcore-tenantisolationviolationexception.md) when it would use another tenant's data.

```csharp
public abstract class TenantContextGuard : IDbConnectionInterceptor, IDbCommandInterceptor, ISaveChangesInterceptor, IInterceptor
```

Implements `IDbConnectionInterceptor`, `IDbCommandInterceptor`, `ISaveChangesInterceptor`, `IInterceptor`.

## Methods

### `Check(DbContext)`

Throws when `context` must not touch the database now.

```csharp
protected abstract void Check(DbContext context)
```

Parameters:

- `context` `DbContext`: The context about to open a connection, run a command or save.

### `ConnectionOpening(DbConnection, ConnectionEventData, InterceptionResult)`

Called just before EF intends to call `Open()`.

```csharp
public virtual InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
```

Parameters:

- `connection` `DbConnection`: The connection.
- `eventData` `ConnectionEventData`: Contextual information about the connection.
- `result` `InterceptionResult`: Represents the current result if one exists. This value will have `IsSuppressed` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `Suppress()`. This value is typically used as the return value for the implementation of this method.

Returns: `InterceptionResult`: If `IsSuppressed` is [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool), then EF will continue as normal. If `IsSuppressed` is [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool), then EF will suppress the operation it was about to perform. An implementation of this method for any interceptor that is not attempting to suppress the operation is to return the `result` value passed in.

### `ConnectionOpeningAsync(DbConnection, ConnectionEventData, InterceptionResult, CancellationToken)`

Called just before EF intends to call `OpenAsync()`.

```csharp
public virtual ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
```

Parameters:

- `connection` `DbConnection`: The connection.
- `eventData` `ConnectionEventData`: Contextual information about the connection.
- `result` `InterceptionResult`: Represents the current result if one exists. This value will have `IsSuppressed` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `Suppress()`. This value is typically used as the return value for the implementation of this method.
- `cancellationToken` `CancellationToken`: A `CancellationToken` to observe while waiting for the task to complete.

Returns: `ValueTask<InterceptionResult>`: If `IsSuppressed` is [false](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool), then EF will continue as normal. If `IsSuppressed` is [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool), then EF will suppress the operation it was about to perform. An implementation of this method for any interceptor that is not attempting to suppress the operation is to return the `result` value passed in.

Exceptions:

- `OperationCanceledException`: If the `CancellationToken` is canceled.

### `FindContext(DbConnection?)`

The context that owns `connection`, for an event that names none. By default, the last context that opened it.

```csharp
protected virtual DbContext? FindContext(DbConnection? connection)
```

Parameters:

- `connection` `DbConnection`: The connection.

Returns: `DbContext`: The context, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) when none is known.

### `NonQueryExecuting(DbCommand, CommandEventData, InterceptionResult<int>)`

Called just before EF intends to call `ExecuteNonQuery()`.

```csharp
public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
```

Parameters:

- `command` `DbCommand`: The command.
- `eventData` `CommandEventData`: Contextual information about the command and execution.
- `result` `InterceptionResult<int>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.

Returns: `InterceptionResult<int>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

### `NonQueryExecutingAsync(DbCommand, CommandEventData, InterceptionResult<int>, CancellationToken)`

Called just before EF intends to call `ExecuteNonQueryAsync()`.

```csharp
public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
```

Parameters:

- `command` `DbCommand`: The command.
- `eventData` `CommandEventData`: Contextual information about the command and execution.
- `result` `InterceptionResult<int>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.
- `cancellationToken` `CancellationToken`: A `CancellationToken` to observe while waiting for the task to complete.

Returns: `ValueTask<InterceptionResult<int>>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in, often using `FromResult<TResult>(TResult)`

Exceptions:

- `OperationCanceledException`: If the `CancellationToken` is canceled.

### `Opening(DbConnection, ConnectionEventData)`

The context a connection event belongs to, recorded for the connection's later commands.

```csharp
protected DbContext? Opening(DbConnection connection, ConnectionEventData eventData)
```

Parameters:

- `connection` `DbConnection`: The connection.
- `eventData` `ConnectionEventData`: The event.

Returns: `DbContext`: The context, or [null](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/null) when none is known.

### `ReaderExecuting(DbCommand, CommandEventData, InterceptionResult<DbDataReader>)`

Called just before EF intends to call `ExecuteReader()`.

```csharp
public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
```

Parameters:

- `command` `DbCommand`: The command.
- `eventData` `CommandEventData`: Contextual information about the command and execution.
- `result` `InterceptionResult<DbDataReader>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.

Returns: `InterceptionResult<DbDataReader>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

### `ReaderExecutingAsync(DbCommand, CommandEventData, InterceptionResult<DbDataReader>, CancellationToken)`

Called just before EF intends to call `ExecuteReaderAsync()`.

```csharp
public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
```

Parameters:

- `command` `DbCommand`: The command.
- `eventData` `CommandEventData`: Contextual information about the command and execution.
- `result` `InterceptionResult<DbDataReader>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.
- `cancellationToken` `CancellationToken`: A `CancellationToken` to observe while waiting for the task to complete.

Returns: `ValueTask<InterceptionResult<DbDataReader>>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in, often using `FromResult<TResult>(TResult)`

Exceptions:

- `OperationCanceledException`: If the `CancellationToken` is canceled.

### `SavingChanges(DbContextEventData, InterceptionResult<int>)`

Called at the start of &lt;see cref="O:DbContext.SaveChanges"&gt;&lt;/see&gt;.

```csharp
public virtual InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
```

Parameters:

- `eventData` `DbContextEventData`: Contextual information about the `DbContext` being used.
- `result` `InterceptionResult<int>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.

Returns: `InterceptionResult<int>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

### `SavingChangesAsync(DbContextEventData, InterceptionResult<int>, CancellationToken)`

Called at the start of &lt;see cref="O:DbContext.SaveChangesAsync"&gt;&lt;/see&gt;.

```csharp
public virtual ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
```

Parameters:

- `eventData` `DbContextEventData`: Contextual information about the `DbContext` being used.
- `result` `InterceptionResult<int>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.
- `cancellationToken` `CancellationToken`: A `CancellationToken` to observe while waiting for the task to complete.

Returns: `ValueTask<InterceptionResult<int>>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

Exceptions:

- `OperationCanceledException`: If the `CancellationToken` is canceled.

### `ScalarExecuting(DbCommand, CommandEventData, InterceptionResult<object>)`

Called just before EF intends to call `ExecuteScalar()`.

```csharp
public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
```

Parameters:

- `command` `DbCommand`: The command.
- `eventData` `CommandEventData`: Contextual information about the command and execution.
- `result` `InterceptionResult<object>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.

Returns: `InterceptionResult<object>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in.

### `ScalarExecutingAsync(DbCommand, CommandEventData, InterceptionResult<object>, CancellationToken)`

Called just before EF intends to call `ExecuteScalarAsync()`.

```csharp
public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
```

Parameters:

- `command` `DbCommand`: The command.
- `eventData` `CommandEventData`: Contextual information about the command and execution.
- `result` `InterceptionResult<object>`: Represents the current result if one exists. This value will have `HasResult` set to [true](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/bool) if some previous interceptor suppressed execution by calling `SuppressWithResult(TResult)`. This value is typically used as the return value for the implementation of this method.
- `cancellationToken` `CancellationToken`: A `CancellationToken` to observe while waiting for the task to complete.

Returns: `ValueTask<InterceptionResult<object>>`: If `HasResult` is false, the EF will continue as normal. If `HasResult` is true, then EF will suppress the operation it was about to perform and use `Result` instead. An implementation of this method for any interceptor that is not attempting to change the result is to return the `result` value passed in, often using `FromResult<TResult>(TResult)`

Exceptions:

- `OperationCanceledException`: If the `CancellationToken` is canceled.
