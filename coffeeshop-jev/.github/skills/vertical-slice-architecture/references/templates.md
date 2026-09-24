# Component templates

Copy-ready code for every component in the architecture. Replace `{App}` with the root namespace,
`{Entity}`/`{Plural}`/`{pluralCamel}` per the naming table in `SKILL.md`.

Contents:

1. [Domain — strong ID](#1-domain--strong-id)
2. [Domain — constrained value object](#2-domain--constrained-value-object)
3. [Domain — entity](#3-domain--entity)
4. [Domain — exception](#4-domain--exception)
5. [Common — DbContext](#5-common--dbcontext)
6. [Common — Vogen EF converters](#6-common--vogen-ef-converters)
7. [Common — entity configuration](#7-common--entity-configuration)
8. [Common — value comparer](#8-common--value-comparer)
9. [Common — DI extension](#9-common--di-extension)
10. [Features — create command](#10-features--create-command)
11. [Features — mutate command](#11-features--mutate-command)
12. [Features — query](#12-features--query)
13. [Features — response DTO and Mapperly mapper](#13-features--response-dto-and-mapperly-mapper)
14. [Host — Program.cs](#14-host--programcs)
15. [Host — GlobalUsings.cs](#15-host--globalusingscs)
16. [Tests — unit](#16-tests--unit)
17. [Tests — integration](#17-tests--integration)
18. [Tests — architecture](#18-tests--architecture)

---

## 1. Domain — strong ID

`Domain/{Entity}Id.cs`. Every entity gets one. A bare `Guid` key fails the architecture tests and
loses the compile-time protection against passing a `PlayerId` where a `GameId` belongs.

```csharp
namespace {App}.Domain;

[ValueObject<Guid>]
public readonly partial record struct {Entity}Id;
```

`partial` is required — Vogen source-generates `From`, `FromNewGuid`, `Parse`, and the validation
plumbing into the other half. `FromNewGuid` exists only because of the assembly-level
`VogenDefaults` in `Program.cs` (see [§14](#14-host--programcs)).

## 2. Domain — constrained value object

`Domain/{Concept}.cs`. Use when a primitive has rules. The `Validate` method is found by
convention, so the name must match exactly.

```csharp
namespace {App}.Domain;

[ValueObject(toPrimitiveCasting: CastOperator.Implicit)]
public readonly partial struct BoardSize
{
    public static readonly BoardSize DefaultBoardSize = From(3);

    private static Validation Validate(int input) =>
        input >= 2 ? Validation.Ok : Validation.Invalid("A board must be at least 2x2");
}
```

`toPrimitiveCasting: CastOperator.Implicit` lets the type be used directly in arithmetic and
comparisons (`i < size`). Omit it when you want callers forced through `.Value`.

For a plain multi-field value with no validation, a positional record struct is lighter and needs
no generator:

```csharp
namespace {App}.Domain;

public readonly record struct BoardPosition(int Row, int Column)
{
    public bool IsWithin(BoardSize boardSize) =>
        Row >= 0 && Row < boardSize.Value && Column >= 0 && Column < boardSize.Value;
}
```

## 3. Domain — entity

`Domain/{Entity}.cs`. Two constructors is the pattern, and the private one is load-bearing: EF
Core materialises rows through it, so it must exist and must not run your guards.

```csharp
using Ardalis.GuardClauses;

namespace {App}.Domain;

public class {Entity}
{
    public {Entity}Id Id { get; init; } = {Entity}Id.FromNewGuid();

    public const int MaxNameLength = 50;
    public string Name { get; init; }

    // Private setters: state changes only through behaviour methods below,
    // so no slice can put the entity into an invalid state.
    public {Entity}State State { get; private set; } = {Entity}State.Initial;

    // EF Core constructor — materialisation only, never call it yourself.
#pragma warning disable CS8618
    private {Entity}() { }
#pragma warning restore CS8618

    public {Entity}({Entity}Id id, string name)
    {
        Guard.Against.StringTooLong(name, MaxNameLength);

        Id = id;
        Name = name;
    }

    public void DoSomething({Concept} input)
    {
        if (State is {Entity}State.Closed)
        {
            throw new Invalid{Entity}OperationException("Already closed");
        }

        // mutate state here; every path leaves the entity valid
    }
}
```

Notes that matter:

- `public const int MaxNameLength` is referenced from **three** places — the guard above, the
  request validator, and the EF configuration's `HasMaxLength`. Keep it a `const` on the entity so
  the three cannot drift.
- Behaviour methods throw domain exceptions rather than returning error codes. The slice decides
  the HTTP mapping (see `wiring.md`).

## 4. Domain — exception

`Domain/{Rule}Exception.cs`.

```csharp
namespace {App}.Domain;

public class Invalid{Entity}OperationException : Exception
{
    public Invalid{Entity}OperationException(string message)
        : base(message) { }
}
```

## 5. Common — DbContext

`Common/EfCore/AppDbContext.cs`. One `DbSet` per **aggregate root** only — owned types and value
objects are reached through their parent and must not get their own `DbSet`.

```csharp
using Microsoft.EntityFrameworkCore;

namespace {App}.Common.EfCore;

public class AppDbContext : DbContext
{
    public DbSet<{Entity}> {Plural} { get; set; } = default!;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Assembly scan: a new IEntityTypeConfiguration is picked up with no
        // registration line. This is why adding a configuration file is enough.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Registers every [EfCoreConverter<T>] from EfCoreConverters.cs.
        configurationBuilder.RegisterAllInEfCoreConverters();
    }
}
```

## 6. Common — Vogen EF converters

`Common/EfCore/EfCoreConverters.cs`. **One attribute per Vogen type that is persisted.** This is
the single most-forgotten file in the architecture.

```csharp
namespace {App}.Common.EfCore;

[EfCoreConverter<Domain.{Entity}Id>]
[EfCoreConverter<Domain.OtherId>]
internal static partial class EfCoreConverters;
```

Vogen generates the `ValueConverter` bodies; `RegisterAllInEfCoreConverters()` in
[§5](#5-common--dbcontext) wires them into the model. Miss the attribute and EF Core treats the ID
as an unmapped complex type — the failure is a model-building exception at startup, not a compile
error.

## 7. Common — entity configuration

`Common/EfCore/Configuration/{Entity}Configuration.cs`. Picked up automatically by the assembly
scan.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace {App}.Common.EfCore.Configuration;

public class {Entity}Configuration : IEntityTypeConfiguration<{Entity}>
{
    public void Configure(EntityTypeBuilder<{Entity}> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength({Entity}.MaxNameLength);
    }
}
```

Owned value object stored as JSON, with the comparer that change tracking requires:

```csharp
using System.Text.Json;

builder.OwnsOne<Board>(
    entity => entity.Board,
    board =>
    {
        board.ToJson();
        board
            .Property(b => b.Value)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Tile[][]>(v, JsonSerializerOptions.Default)
                     ?? Array.Empty<Tile[]>(),
                new TileArrayComparer()   // required — see §8
            );
    }
);
```

## 8. Common — value comparer

`Common/EfCore/Configuration/{Type}Comparer.cs`. Needed for any converted **mutable reference**
value (arrays, collections). EF Core compares snapshots by reference by default, so without a
comparer an in-place mutation like `board.Value[0][0] = Tile.X` is never detected and
`SaveChangesAsync` writes nothing.

```csharp
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace {App}.Common.EfCore.Configuration;

public class TileArrayComparer : ValueComparer<Tile[][]>
{
    public TileArrayComparer()
        : base(
            (a, b) => a != null && b != null && a.SelectMany(r => r).SequenceEqual(b.SelectMany(r => r)),
            v => v.SelectMany(r => r).Aggregate(0, HashCode.Combine),
            v => v.Select(r => r.ToArray()).ToArray()   // deep snapshot
        ) { }
}
```

The third argument is the snapshot factory. A shallow copy here reintroduces the bug, because the
snapshot and the live value share the inner arrays.

## 9. Common — DI extension

`Common/EfCore/DependencyInjectionExtensions.cs`. Keeps `Program.cs` to one call per concern.

```csharp
using Microsoft.EntityFrameworkCore;

namespace {App}.Common.EfCore;

public static class DependencyInjectionExtensions
{
    public static void AddAppDbContext(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
        });
    }
}
```

## 10. Features — create command

`Features/{Plural}/New{Entity}Command.cs`. Returns `Created` with a location header and **no
body**, so it needs no response DTO or mapper.

```csharp
namespace {App}.Features.{Plural};

public sealed record New{Entity}Request(string Name);

internal sealed class New{Entity}RequestValidator : AbstractValidator<New{Entity}Request>
{
    public New{Entity}RequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength({Entity}.MaxNameLength);
    }
}

internal sealed class New{Entity}Command(AppDbContext db)
    : Endpoint<New{Entity}Request, Results<Created, BadRequest>>
{
    public override void Configure()
    {
        Post("/{pluralCamel}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(
        New{Entity}Request request,
        CancellationToken cancellationToken
    )
    {
        var entity = new {Entity}({Entity}Id.FromNewGuid(), request.Name);
        db.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        // Concrete form for Games: $"/games/{entity.Id}"
        await SendResultAsync(TypedResults.Created($"/games/{entity.Id}"));
    }
}
```

The validator is `internal sealed`, sits in the same file, and is discovered automatically by
FastEndpoints because it is in the same assembly — there is no registration line.

**On braces below:** C# routes and interpolated strings use `{}`, which collides with this
document's `{Placeholder}` convention. Where the two would be ambiguous, the snippets show the
concrete `Game`/`Games`/`games` form from the reference template rather than a placeholder.

## 11. Features — mutate command

`Features/{Plural}/{Verb}{Entity}Command.cs`. The strong ID in the request is bound from the route
by FastEndpoints when the property name matches the route parameter.

```csharp
using {App}.Features.{Plural}.Common;

namespace {App}.Features.{Plural};

public sealed record {Verb}{Entity}Request({Entity}Id {Entity}Id, {Concept} Input);

internal sealed class {Verb}{Entity}Command(AppDbContext db)
    : Endpoint<{Verb}{Entity}Request, Results<Ok<{Entity}Response>, NotFound>>
{
    public override void Configure()
    {
        // Route param name must match the Request property name for binding.
        Post("/games/{GameId}/play-turn");
        Summary(x =>
        {
            x.Description = "One line for Swagger";
        });
        AllowAnonymous();
    }

    public override async Task HandleAsync(
        {Verb}{Entity}Request request,
        CancellationToken cancellationToken
    )
    {
        var entity = await db.FindAsync<{Entity}>(request.{Entity}Id, cancellationToken);

        if (entity is null)
        {
            await SendResultAsync(TypedResults.NotFound());
            return;
        }

        entity.DoSomething(request.Input);              // invariants enforced in Domain
        await db.SaveChangesAsync(cancellationToken);

        var output = entity.ToResponse();               // in-memory: entity already loaded
        await SendResultAsync(TypedResults.Ok(output));
    }
}
```

`FindAsync` returns the tracked instance if already loaded, so it is the right call on the write
path. Do not use `AsNoTracking()` in a command; the change tracker is what makes
`SaveChangesAsync` work.

## 12. Features — query

`Features/{Plural}/View{Entity}Query.cs`. Projects in the database — this is the whole reason a
Mapperly `IQueryable` overload exists.

```csharp
using Microsoft.EntityFrameworkCore;
using {App}.Features.{Plural}.Common;

namespace {App}.Features.{Plural};

public sealed record View{Entity}Request({Entity}Id {Entity}Id);

internal sealed class View{Entity}Query(AppDbContext db)
    : Endpoint<View{Entity}Request, Results<Ok<{Entity}Response>, NotFound>>
{
    public override void Configure()
    {
        Get("/games/{GameId}");
        Summary(x =>
        {
            x.Description = "One line for Swagger";
        });
        AllowAnonymous();
    }

    public override async Task HandleAsync(
        View{Entity}Request request,
        CancellationToken cancellationToken
    )
    {
        var response = await db
            .{Plural}.AsNoTracking()
            .Where(x => x.Id == request.{Entity}Id)
            .ProjectToResponse()                        // expression: becomes SQL columns
            .FirstOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            await SendResultAsync(TypedResults.NotFound());
            return;
        }

        await SendResultAsync(TypedResults.Ok(response));
    }
}
```

Command vs query mapping, stated once because getting it backwards is the common mistake:

| Path | Load | Map with | Why |
| --- | --- | --- | --- |
| Command | `FindAsync` (tracked) | `entity.ToResponse()` | entity is in memory and must be tracked to save |
| Query | `IQueryable` + `AsNoTracking()` | `.ProjectToResponse()` | projection is translated to SQL; no entity materialised |

## 13. Features — response DTO and Mapperly mapper

`Features/{Plural}/Common/{Entity}Response.cs`. Scoped to the slice group — another group needing
this DTO is a signal to reconsider the group boundary, not to import across groups.

```csharp
namespace {App}.Features.{Plural}.Common;

public record {Entity}Response
{
    public required string Name { get; set; }
    public {Entity}State State { get; set; }
    public char[][]? Board { get; set; }
}

[Mapper]
public static partial class {Entity}ResponseMapper
{
    // Both members are needed: the instance overload for commands,
    // the IQueryable overload for queries. Mapperly generates both bodies.
    [MapProperty(nameof({Entity}.Board), nameof(Board), Use = nameof(MapBoard))]
    public static partial {Entity}Response ToResponse(this {Entity} source);

    public static partial IQueryable<{Entity}Response> ProjectToResponse(
        this IQueryable<{Entity}> q
    );

    [UserMapping(Default = false)]
    private static char[][]? MapBoard(Board? board) =>
        board?.Value.Select(row => row.Select(GetTileChar).ToArray()).ToArray();

    private static char GetTileChar(Tile tile) =>
        tile switch
        {
            Tile.Empty => ' ',
            Tile.X => 'X',
            Tile.O => 'O',
            _ => '?',
        };
}
```

Why Mapperly rather than a hand-written or reflection-based mapper: the generated code is real,
navigable C#, so "find usages" on a domain property reaches the response at compile time. Renaming
a domain property breaks the build instead of silently emptying an API field.

`[UserMapping(Default = false)]` stops Mapperly from picking the custom method up for unrelated
properties of the same type. A custom method used inside `ProjectToResponse` must be expression
translatable, or that overload will not compile.

## 14. Host — Program.cs

`Program.cs`. Four things here exist only to serve code elsewhere; all four are commented.

```csharp
using System.Text.Json.Serialization;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;

// Enables {Entity}Id.FromNewGuid() on every Vogen Guid value object in the assembly.
[assembly: VogenDefaults(customizations: Customizations.AddFactoryMethodForGuids)]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFastEndpoints(options =>
{
    // Source-generated endpoint list: reflection-free discovery, so new slices
    // are registered by rebuilding, not by editing this file.
    options.SourceGeneratorDiscoveredTypes.AddRange({App}.DiscoveredTypes.All);
});

// Enums as strings — configured three times because FastEndpoints, minimal APIs
// and MVC each read their own options object.
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.SwaggerDocument();
builder.Services.AddAppDbContext(builder.Configuration);

var app = builder.Build();

app.UseFastEndpoints(config =>
{
    config.Serializer.Options.Converters.Add(new JsonStringEnumConverter());
});
app.UseSwaggerGen();

#if DEBUG
using (var dbScope = app.Services.CreateScope())
{
    var db = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.MigrateAsync();
}
#endif

app.Run();

// Required by WebApplicationFactory<Program> in the integration tests. Deleting
// this line breaks that project, with no error in this one.
public partial class Program;
```

## 15. Host — GlobalUsings.cs

`GlobalUsings.cs`. This is why slice files open straight into a namespace declaration with almost
no `using` lines — a deliberate readability choice. Add to it when a namespace is needed by most
slices; keep genuinely local usings in the file that needs them.

```csharp
global using System;
global using FastEndpoints;
global using FluentValidation;
global using Microsoft.AspNetCore.Http.HttpResults;
global using Riok.Mapperly.Abstractions;
global using {App}.Common.EfCore;
global using {App}.Domain;
global using Vogen;
```

## 16. Tests — unit

`tests/{App}.Unit.Tests/Domain/{Entity}Tests.cs`. Only `Domain` is unit tested: it has no
dependencies, so tests need no host, no database, and no mocks.

```csharp
using FluentAssertions;
using {App}.Domain;

namespace {App}.Unit.Tests.Domain;

public class {Entity}Tests
{
    [Fact]
    public void {Entity}_DoSomething_MutatesState()
    {
        // Arrange
        var entity = new {Entity}({Entity}Id.FromNewGuid(), "Some Name");

        // Act
        entity.DoSomething(input);

        // Assert
        entity.State.Should().Be({Entity}State.Expected);
    }

    [Fact]
    public void {Entity}_DoSomething_WhenClosed_Throws()
    {
        var entity = new {Entity}({Entity}Id.FromNewGuid(), "Some Name");
        entity.Close();

        var act = () => entity.DoSomething(input);

        act.Should().Throw<Invalid{Entity}OperationException>();
    }
}
```

Every invariant enforced in a behaviour method deserves a test here — it is far cheaper than
proving the same rule through the HTTP layer.

## 17. Tests — integration

One test class per use case, mirroring the slice path: `Features/{Plural}/{UseCase}Tests.cs`. The
base class starts a real Postgres container, so these tests exercise the actual SQL, Vogen
converters, and value comparers — the parts that unit tests cannot reach.

```csharp
using {App}.Features.{Plural};
using {App}.Features.{Plural}.Common;

namespace {App}.Integration.Tests.Features.{Plural};

public class {Verb}{Entity}CommandTests : IntegrationTestBase
{
    [Fact]
    public async Task {Verb}{Entity}_UpdatesState()
    {
        // Arrange — seed through the API when a create endpoint exists…
        var created = await Client.PostAsJsonAsync(
            "/games",
            new New{Entity}Request("Some Name"),
            JsonSerializerOptions
        );
        var idRaw = created.Headers.Location?.ToString().Split('/').Last();
        idRaw.Should().NotBeNullOrEmpty();
        var id = {Entity}Id.Parse(idRaw);

        // …or through a DB scope when the setup state is not reachable via HTTP.
        // await using (var scope = NewScope())
        // {
        //     var db = scope.GetDbContext();
        //     db.{Plural}.Add(entity);
        //     await db.SaveChangesAsync();
        // }

        // Act
        var response = await Client.PostAsJsonAsync(
            $"/games/{id}/play-turn",
            new {Verb}{Entity}Request(id, input),
            JsonSerializerOptions
        );

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<{Entity}Response>(
            JsonSerializerOptions
        );
        body!.State.Should().Be({Entity}State.Expected);

        // A fresh scope reads committed data rather than the tracked graph — this is
        // what catches a missing ValueComparer, where the HTTP response looks right
        // but nothing was persisted.
        await using (var verifyScope = NewScope())
        {
            var db = verifyScope.GetDbContext();
            var persisted = await db.{Plural}.FindAsync(id);
            persisted!.ToResponse().Should().BeEquivalentTo(body);
        }
    }
}
```

Pass `JsonSerializerOptions` (from the base class) on every call — it carries the
`JsonStringEnumConverter`, so omitting it makes enum round-trips fail in a confusing way.

Supporting infrastructure, written once per solution:

```csharp
// IntegrationTestBase.cs
public class IntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder().Build();
    private TestAppFactory _factory = null!;

    protected HttpClient Client = null!;

    protected static readonly JsonSerializerOptions JsonSerializerOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected AsyncServiceScope NewScope() => _factory.Services.CreateAsyncScope();

    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        _factory = new TestAppFactory(_postgreSqlContainer.GetConnectionString());
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions());

        await using var scope = NewScope();
        var db = scope.GetDbContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }
}

// TestAppFactory.cs — swaps the connection string for the container's
public class TestAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
            );
            services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
}

// ScopeExtensions.cs
public static class ScopeExtensions
{
    public static AppDbContext GetDbContext(this AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();
}
```

## 18. Tests — architecture

`tests/{App}.Architecture.Tests/`. Enforces the five invariants from `SKILL.md` so the structure
cannot rot silently. The template declares which namespace is which VSA concept and the
`Hona.ArchitectureTests` package derives the rules.

```csharp
namespace {App}.Architecture.Tests;

public class VerticalSliceArchitectureTests
{
    [Fact]
    public void VerticalSliceArchitecture()
    {
        Ensure
            .VerticalSliceArchitecture(x =>
            {
                x.Domain = new NamespacePart(AppAssembly, ".Domain");
                x.Common = new NamespacePart(AppAssembly, ".Common");
                x.Features = new NamespacePart(AppAssembly, ".Features");
            })
            .Assert();
    }
}
```

The generated suite covers, among others: `Domain_DependsOn_Nothing`, `Common_DependOn_Nothing`,
`Features_DependOn_Common`, `Features_DontDependOn_EachOther`, `UseCases_Are_CqrsNamed`,
`UseCases_Are_Sealed`, `UseCases_AreInternal`, `UseCases_Have_RequestDto`,
`UseCases_HaveResponseDto`, `DomainEntity_Id_IsStrongId`.

If a new component makes one of these fail, the component is in the wrong place — move it rather
than relaxing the rule.
</content>
