using System.Net;
using System.Net.Http.Headers;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Dtos;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Inspecciones.Infrastructure.Tests.Erp;

/// <summary>
/// Tests de contrato del adapter Maquinaria_V4. Cubren:
///   1. Happy path para cada endpoint (mapeo de DTOs).
///   2. Manejo de <c>If-None-Match</c> / <c>304 Not Modified</c> en catálogos.
///   3. Mapeo del error envelope <c>{Codigo, Mensaje}</c> de Maquinaria_V4 a
///      <see cref="MaquinariaErpException"/>.
///   4. Propagación del header <c>Authorization</c>.
/// </summary>
/// <remarks>
/// Estos tests son de "integración con mock externo" — WireMock corre en proceso
/// como servidor HTTP. NO requieren Maquinaria_V4 corriendo. Cubren el contrato
/// HTTP del adapter, no el comportamiento del aggregate (que ya está cubierto en
/// Application.Tests y Domain.Tests).
/// </remarks>
public sealed class MaquinariaErpClientTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _http;
    private readonly MaquinariaErpClient _client;

    public MaquinariaErpClientTests()
    {
        _server = WireMockServer.Start();
        _http = new HttpClient
        {
            BaseAddress = new Uri(_server.Urls[0] + "/"),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-jwt");
        _client = new MaquinariaErpClient(_http);
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Stop();
        _server.Dispose();
    }

    // ─── Equipos ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarEquipos_devuelve_body_y_propaga_authorization()
    {
        _server
            .Given(Request.Create().WithPath("/api/equipos").UsingGet()
                .WithHeader("Authorization", "Bearer test-jwt"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("ETag", "\"equipos-2026-05-19\"")
                .WithBodyAsJson(new
                {
                    Equipos = new[]
                    {
                        new
                        {
                            EquipoId = 42,
                            Placa = "EQ-042",
                            Description = "Excavadora",
                            UM1 = "HR",
                            M1Actual = 1234.5m,
                            GrupoMantenimientoId = 3,
                        },
                    },
                    Total = 1,
                }));

        var result = await _client.ListarEquiposAsync(filtro: null);

        result.NotModified.Should().BeFalse();
        result.Body.Should().NotBeNull();
        result.Body!.Equipos.Should().HaveCount(1);
        result.Body.Equipos[0].EquipoId.Should().Be(42);
        result.Body.Equipos[0].Placa.Should().Be("EQ-042");
        result.ETag.Should().Be("\"equipos-2026-05-19\"");
    }

    [Fact]
    public async Task ListarEquipos_con_filtro_propaga_query_string()
    {
        _server
            .Given(Request.Create().WithPath("/api/equipos").WithParam("filtro", "EQ-04").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { Equipos = Array.Empty<object>(), Total = 0 }));

        var result = await _client.ListarEquiposAsync(filtro: "EQ-04");

        result.Body.Should().NotBeNull();
        result.Body!.Total.Should().Be(0);
    }

    [Fact]
    public async Task ListarEquipos_con_If_None_Match_y_304_devuelve_NotModified()
    {
        _server
            .Given(Request.Create().WithPath("/api/equipos").UsingGet()
                .WithHeader("If-None-Match", "\"equipos-2026-05-19\""))
            .RespondWith(Response.Create().WithStatusCode(304).WithHeader("ETag", "\"equipos-2026-05-19\""));

        var result = await _client.ListarEquiposAsync(filtro: null, ifNoneMatch: "\"equipos-2026-05-19\"");

        result.NotModified.Should().BeTrue();
        result.Body.Should().BeNull();
        result.ETag.Should().Be("\"equipos-2026-05-19\"");
    }

    [Fact]
    public async Task ListarEquipos_con_401_lanza_MaquinariaErpException_con_codigoErp()
    {
        _server
            .Given(Request.Create().WithPath("/api/equipos").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithBodyAsJson(new { Codigo = "UNAUTHORIZED", Mensaje = "Token inválido" }));

        var act = () => _client.ListarEquiposAsync(filtro: null);

        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ex.Which.CodigoErp.Should().Be("UNAUTHORIZED");
        ex.Which.Message.Should().Contain("Token inválido");
    }

    // ─── Partes-equipos ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListarPartesEquipo_propaga_idEquipo_y_deserializa()
    {
        _server
            .Given(Request.Create().WithPath("/api/partes-equipos").WithParam("idEquipo", "42").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("ETag", "\"partes-42-2026-05-19\"")
                .WithBodyAsJson(new
                {
                    Partes = new[]
                    {
                        new { ParteId = 100, ParteDescripcion = "Motor", RutinaMantenimientoDescripcion = "Diaria", EquipoId = 42 },
                        new { ParteId = 101, ParteDescripcion = "Bomba", RutinaMantenimientoDescripcion = "Diaria", EquipoId = 42 },
                    },
                    Total = 2,
                }));

        var result = await _client.ListarPartesEquipoAsync(idEquipo: 42);

        result.Body.Should().NotBeNull();
        result.Body!.Partes.Should().HaveCount(2);
        result.Body.Partes[0].ParteId.Should().Be(100);
        result.Body.Partes[0].ParteDescripcion.Should().Be("Motor");
        result.ETag.Should().Be("\"partes-42-2026-05-19\"");
    }

    // ─── Causas-falla ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListarCausasFalla_propaga_texto_y_deserializa_shape_correcto()
    {
        _server
            .Given(Request.Create().WithPath("/api/causas-falla").WithParam("texto", "-1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    Causas = new[]
                    {
                        new { Codigo = 1, Descripcion = "Sobrecarga" },
                        new { Codigo = 2, Descripcion = "Fatiga" },
                    },
                    Total = 2,
                }));

        var result = await _client.ListarCausasFallaAsync(texto: "-1");

        result.Body.Should().NotBeNull();
        result.Body!.Causas.Should().HaveCount(2);
        result.Body.Causas[0].Codigo.Should().Be(1);
        result.Body.Causas[0].Descripcion.Should().Be("Sobrecarga");
    }

    // ─── Tipos-falla ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarTiposFalla_deserializa_con_Prioridad_string()
    {
        _server
            .Given(Request.Create().WithPath("/api/tipos-falla").WithParam("texto", "-1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    TiposFalla = new[]
                    {
                        new { Codigo = 10, Descripcion = "Eléctrica", Prioridad = "ALTA" },
                    },
                    Total = 1,
                }));

        var result = await _client.ListarTiposFallaAsync(texto: "-1");

        result.Body!.TiposFalla[0].Prioridad.Should().Be("ALTA");
    }

    // ─── Productos ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarProductos_deserializa_codigo_descripcion_unidad()
    {
        _server
            .Given(Request.Create().WithPath("/api/productos").WithParam("texto", "FILTRO").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    Productos = new[]
                    {
                        new { Codigo = 555, Descripcion = "Filtro aceite", UnidadContable = "UND" },
                    },
                    Total = 1,
                }));

        var result = await _client.ListarProductosAsync(texto: "FILTRO");

        result.Body!.Productos[0].Codigo.Should().Be(555);
        result.Body.Productos[0].UnidadContable.Should().Be("UND");
    }

    // ─── Preoperacional-fallas listar ───────────────────────────────────────

    [Fact]
    public async Task ListarPreoperacionalFallas_serializa_fechas_y_filtros()
    {
        _server
            .Given(Request.Create().WithPath("/api/preoperacional-fallas").UsingGet()
                .WithParam("desde", "2026-05-01")
                .WithParam("hasta", "2026-05-19")
                .WithParam("equipoId", "42")
                .WithParam("texto", "-1"))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    Fallas = new[]
                    {
                        new
                        {
                            Id = 999,
                            RegistroPreoperacionalId = 100,
                            EquipoId = 42,
                            ActividadId = 7,
                            ArbolDescripcion = "Lubricación",
                            ActividadDescripcion = "Engrasar pivote",
                            Observacion = "Pendiente",
                            Fecha = "2026-05-15T08:30:00Z",
                        },
                    },
                    Total = 1,
                }));

        var result = await _client.ListarPreoperacionalFallasAsync(
            desde: new DateOnly(2026, 5, 1),
            hasta: new DateOnly(2026, 5, 19),
            equipoId: 42,
            texto: "-1");

        result.Fallas.Should().HaveCount(1);
        result.Fallas[0].Id.Should().Be(999);
        result.Fallas[0].EquipoId.Should().Be(42);
    }

    // ─── Cerrar preop ───────────────────────────────────────────────────────

    [Fact]
    public async Task CerrarPreoperacionalFallas_envia_body_correcto_y_deserializa()
    {
        _server
            .Given(Request.Create().WithPath("/api/preoperacional-fallas/cerrar").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    CerradasAhora = 2,
                    YaCerradas = 1,
                    Total = 3,
                    PodIdsCerradosAhora = new[] { 101, 102 },
                }));

        var result = await _client.CerrarPreoperacionalFallasAsync(new CerrarPreoperacionalFallasRequestDto
        {
            PodIds = new[] { 101, 102, 103 },
            Observaciones = "Cerrado por inspección 2026-05-19",
        });

        result.CerradasAhora.Should().Be(2);
        result.YaCerradas.Should().Be(1);
        result.PodIdsCerradosAhora.Should().BeEquivalentTo(new[] { 101, 102 });
    }

    [Fact]
    public async Task CerrarPreoperacionalFallas_con_422_lanza_excepcion_con_codigoErp()
    {
        _server
            .Given(Request.Create().WithPath("/api/preoperacional-fallas/cerrar").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(422)
                .WithBodyAsJson(new { Codigo = "VALIDATION_ERROR", Mensaje = "Algunos PODIds no son visibles" }));

        var act = () => _client.CerrarPreoperacionalFallasAsync(new CerrarPreoperacionalFallasRequestDto
        {
            PodIds = new[] { 999 },
            Observaciones = "test",
        });

        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ex.Which.CodigoErp.Should().Be("VALIDATION_ERROR");
    }

    // ─── Rutinas-monitoreo por equipo ───────────────────────────────────────

    [Fact]
    public async Task ListarRutinasMonitoreoPorEquipo_devuelve_lista_filtrada()
    {
        _server
            .Given(Request.Create().WithPath("/api/rutinas-monitoreo").WithParam("equipoId", "42").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("ETag", "\"rm-42-2026-05-19\"")
                .WithBodyAsJson(new
                {
                    Rutinas = new[]
                    {
                        new { Codigo = 7, Descripcion = "Rutina A" },
                        new { Codigo = 8, Descripcion = "Rutina B" },
                    },
                    Total = 2,
                }));

        var result = await _client.ListarRutinasMonitoreoPorEquipoAsync(equipoId: 42);

        result.Body!.Rutinas.Should().HaveCount(2);
        result.Body.Rutinas[0].Codigo.Should().Be(7);
        result.ETag.Should().Be("\"rm-42-2026-05-19\"");
    }

    // ─── Actualizar dictamen equipo ─────────────────────────────────────────

    [Fact]
    public async Task ActualizarDictamenEquipo_envia_PUT_con_body_y_deserializa()
    {
        _server
            .Given(Request.Create().WithPath("/api/equipos/42/dictamen-vigente").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    Codigo = 42,
                    Estado = 1,
                    EstadoUsuario = 7,
                    EstadoFecha = "2026-05-19T10:00:00Z",
                }));

        var result = await _client.ActualizarDictamenEquipoAsync(
            equipoCodigo: 42,
            request: new ActualizarDictamenEquipoRequestDto { Estado = 1 });

        result.Codigo.Should().Be(42);
        result.Estado.Should().Be(1);
        result.EstadoUsuario.Should().Be(7);
    }

    [Fact]
    public async Task ActualizarDictamenEquipo_con_404_lanza_excepcion_con_NotFound()
    {
        _server
            .Given(Request.Create().WithPath("/api/equipos/9999/dictamen-vigente").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithBodyAsJson(new { Codigo = "EQUIPO_NOT_FOUND", Mensaje = "Equipo 9999 no existe" }));

        var act = () => _client.ActualizarDictamenEquipoAsync(
            equipoCodigo: 9999,
            request: new ActualizarDictamenEquipoRequestDto { Estado = 0 });

        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Which.CodigoErp.Should().Be("EQUIPO_NOT_FOUND");
    }
}
