using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>PATCH /api/v1/inspecciones/{id}/hallazgos/{hid}</c> contra
/// la app real con Postgres en Testcontainers. Spec slice 2 §9.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class ActualizarHallazgoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn = new(2026, 5, 6, 10, 0, 0, TimeSpan.FromHours(-5));

    private static object RequestBodyConIntervencion() => new
    {
        actividadId          = (object?)null,
        actividadDescripcion = (object?)null,
        novedadTecnica       = "Fisura en bloque motor",
        accionRequerida      = "RequiereIntervencion",
        accionCorrectiva     = "Reemplazar bloque",
        tipoFallaId          = 10,
        causaFallaId         = 5,
        observacionCampo     = (object?)null,
        ubicacion            = (object?)null
    };

    private static object RequestBodyConSeguimiento() => new
    {
        actividadId          = (object?)null,
        actividadDescripcion = (object?)null,
        novedadTecnica       = "Vibración leve en eje",
        accionRequerida      = "RequiereSeguimiento",
        accionCorrectiva     = (object?)null,
        tipoFallaId          = (object?)null,
        causaFallaId         = (object?)null,
        observacionCampo     = (object?)null,
        ubicacion            = (object?)null
    };

    /// <summary>
    /// Siembra una inspección en EnEjecucion con un hallazgo NoRequiereIntervencion.
    /// Devuelve (inspeccionId, hallazgoId).
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId)> SembrarInspeccionConHallazgo(
        int equipoId = 15001)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        var inspeccionIniciada = new InspeccionIniciada_v1(
            InspeccionId: inspeccionId,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: equipoId,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: "rmartinez",
            ProyectoId: 3,
            Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
            IniciadaEn: CapturadoEn,
            FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null);

        var hallazgoRegistrado = new HallazgoRegistrado_v1(
            InspeccionId: inspeccionId,
            HallazgoId: hallazgoId,
            Origen: OrigenHallazgo.Manual,
            NovedadPreopOrigenId: null,
            ParteEquipoId: 77,
            ActividadId: null,
            ActividadDescripcion: "Revisión visual",
            NovedadTecnica: "Desgaste inicial en manguera",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: "rmartinez",
            RegistradoEn: CapturadoEn);

        session.Events.StartStream<Inspeccion>(inspeccionId, inspeccionIniciada, hallazgoRegistrado);

        session.Store(new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
            RutinaTecnicaId: 18,
            Partes: new List<ParteEquipoLocal> { new(77, "MANGUERA-HID", "Manguera hidráulica") }));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Happy path E2E HTTP — §6.1 via PATCH
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_inspecciones_id_hallazgos_hid_happy_path_responde_200_OK()
    {
        // Given: inspección con un hallazgo Manual/NoRequiereIntervencion
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 15001);
        var clientCommandId = Guid.NewGuid().ToString();

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}")
        {
            Content = JsonContent.Create(RequestBodyConIntervencion())
        };
        request.Headers.Add("X-Client-Command-Id", clientCommandId);

        // When
        var response = await client.SendAsync(request);

        // Then: 200 OK con hallazgoId y actualizadoEn
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaActualizarHallazgo>();
        resultado.Should().NotBeNull();
        resultado!.HallazgoId.Should().Be(hallazgoId);
        resultado.ActualizadoEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(30));
    }

    // ─────────────────────────────────────────────────────────────────────
    // PRE-1 — inspección no existe → 404
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_inspecciones_id_hallazgos_hid_inspeccion_inexistente_responde_404()
    {
        // Given: InspeccionId que no existe en el stream
        var inspeccionIdDesconocido = Guid.NewGuid();
        var hallazgoIdDesconocido = Guid.NewGuid();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/inspecciones/{inspeccionIdDesconocido}/hallazgos/{hallazgoIdDesconocido}")
        {
            Content = JsonContent.Create(RequestBodyConSeguimiento())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────
    // PRE-3 — HallazgoId no existe → 404
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_inspecciones_id_hallazgos_hid_hallazgo_inexistente_responde_404()
    {
        // Given: inspección existente pero HallazgoId desconocido
        var (inspeccionId, _) = await SembrarInspeccionConHallazgo(equipoId: 15002);
        var hallazgoDesconocido = Guid.NewGuid();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoDesconocido}")
        {
            Content = JsonContent.Create(RequestBodyConSeguimiento())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────
    // I-H4 — RequiereIntervencion sin tipo/causa → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_inspecciones_id_hallazgos_hid_RequiereIntervencion_sin_tipo_causa_responde_422()
    {
        // Given
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 15003);
        var client = factory.CreateClient();

        var bodyInvalido = new
        {
            actividadId      = (object?)null,
            actividadDescripcion = (object?)null,
            novedadTecnica   = "Falla crítica",
            accionRequerida  = "RequiereIntervencion",
            accionCorrectiva = "Reparar",
            tipoFallaId      = (object?)null,
            causaFallaId     = (object?)null,
            observacionCampo = (object?)null,
            ubicacion        = (object?)null
        };

        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}")
        {
            Content = JsonContent.Create(bodyInvalido)
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sin X-Client-Command-Id → 400
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_inspecciones_id_hallazgos_hid_sin_header_ClientCommandId_responde_400()
    {
        // Given: inspección existente, pero falta el header requerido (ADR-008)
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 15004);
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}")
        {
            Content = JsonContent.Create(RequestBodyConSeguimiento())
        };
        // No se agrega el header X-Client-Command-Id

        // When
        var response = await client.SendAsync(request);

        // Then
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>DTO local de lectura — independiente del namespace de la API.</summary>
    private sealed record RespuestaActualizarHallazgo(
        Guid HallazgoId,
        DateTimeOffset ActualizadoEn);
}
