using FluentAssertions;
using Inspecciones.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Wolverine;

namespace Inspecciones.Infrastructure.Tests.Auth;

/// <summary>
/// Tests rojos del slice mt-4 §6.1, §6.2, §6.3, §6.4, §6.5, §6.6.
///
/// Cubre el wiring de captura del bearer entrante al envelope outbox (FU-60).
///   §6.1 — middleware pasa Authorization al IncomingBearerCarrier dentro del scope.
///   §6.2 — sin header → carrier vacío.
///   §6.3 — esquema no Bearer → carrier vacío.
///   §6.4 — ForwardAuthEnvelopeRule propaga al envelope cuando hay bearer.
///   §6.5 — rule no clobberea header pre-existente del publisher.
///   §6.6 — sin carrier no añade header.
///
/// Sin Postgres / Marten / Wolverine host real — todos los tests son in-process.
/// </summary>
public sealed class CaptureBearerForOutboxMiddlewareTests
{
    // ─── §6.1 — middleware pasa Authorization al IncomingBearerCarrier ─────────

    [Fact]
    public async Task Middleware_con_Bearer_setea_carrier_en_scope_del_next()
    {
        // GIVEN
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Bearer jwt-empresa-7";

        string? capturado = null;
        var middleware = new CaptureBearerForOutboxMiddleware(_ =>
        {
            capturado = IncomingBearerCarrier.GetForwardedAuth();
            return Task.CompletedTask;
        });

        // WHEN
        await middleware.Invoke(ctx);

        // THEN
        capturado.Should().Be("Bearer jwt-empresa-7");
        // Post-scope: carrier limpio.
        IncomingBearerCarrier.GetForwardedAuth().Should().BeNull();
    }

    // ─── §6.2 — sin header → carrier vacío ─────────────────────────────────────

    [Fact]
    public async Task Middleware_sin_header_Authorization_no_setea_carrier()
    {
        var ctx = new DefaultHttpContext();

        string? capturado = "sentinel";
        var middleware = new CaptureBearerForOutboxMiddleware(_ =>
        {
            capturado = IncomingBearerCarrier.GetForwardedAuth();
            return Task.CompletedTask;
        });

        await middleware.Invoke(ctx);

        capturado.Should().BeNull();
    }

    // ─── §6.3 — esquema no Bearer → no captura ─────────────────────────────────

    [Fact]
    public async Task Middleware_con_esquema_Basic_no_setea_carrier()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        string? capturado = "sentinel";
        var middleware = new CaptureBearerForOutboxMiddleware(_ =>
        {
            capturado = IncomingBearerCarrier.GetForwardedAuth();
            return Task.CompletedTask;
        });

        await middleware.Invoke(ctx);

        capturado.Should().BeNull();
    }

    [Fact]
    public async Task Middleware_con_header_vacio_no_setea_carrier()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "";

        string? capturado = "sentinel";
        var middleware = new CaptureBearerForOutboxMiddleware(_ =>
        {
            capturado = IncomingBearerCarrier.GetForwardedAuth();
            return Task.CompletedTask;
        });

        await middleware.Invoke(ctx);

        capturado.Should().BeNull();
    }

    // ─── §6.4 — ForwardAuthEnvelopeRule propaga al envelope ────────────────────

    [Fact]
    public void EnvelopeRule_con_carrier_seteado_propaga_al_envelope()
    {
        // GIVEN
        using var _ = IncomingBearerCarrier.SetForCurrentScope("Bearer jwt-empresa-7");
        var envelope = new Envelope();

        // WHEN
        var rule = new ForwardAuthEnvelopeRule();
        rule.Modify(envelope);

        // THEN
        envelope.Headers.Should().ContainKey("X-Forwarded-Authorization");
        envelope.Headers["X-Forwarded-Authorization"].Should().Be("Bearer jwt-empresa-7");
    }

    // ─── §6.5 — rule no sobreescribe header pre-existente ──────────────────────

    [Fact]
    public void EnvelopeRule_no_sobreescribe_header_si_publisher_ya_lo_seteo()
    {
        using var _ = IncomingBearerCarrier.SetForCurrentScope("Bearer jwt-A");
        var envelope = new Envelope();
        envelope.Headers["X-Forwarded-Authorization"] = "Bearer jwt-B";

        var rule = new ForwardAuthEnvelopeRule();
        rule.Modify(envelope);

        envelope.Headers["X-Forwarded-Authorization"].Should().Be("Bearer jwt-B");
    }

    // ─── §6.6 — sin carrier no añade header ────────────────────────────────────

    [Fact]
    public void EnvelopeRule_sin_carrier_no_añade_header()
    {
        // GIVEN: sin SetForCurrentScope — carrier vacío.
        var envelope = new Envelope();

        // WHEN
        var rule = new ForwardAuthEnvelopeRule();
        rule.Modify(envelope);

        // THEN
        envelope.Headers.Should().NotContainKey("X-Forwarded-Authorization");
    }

    // ─── Extra: IncomingBearerCarrier scope nesting ────────────────────────────

    [Fact]
    public void IncomingBearerCarrier_scope_nesting_restaura_valor_anterior_al_dispose()
    {
        using (var outer = IncomingBearerCarrier.SetForCurrentScope("Bearer jwt-outer"))
        {
            IncomingBearerCarrier.GetForwardedAuth().Should().Be("Bearer jwt-outer");
            using (var inner = IncomingBearerCarrier.SetForCurrentScope("Bearer jwt-inner"))
            {
                IncomingBearerCarrier.GetForwardedAuth().Should().Be("Bearer jwt-inner");
            }
            IncomingBearerCarrier.GetForwardedAuth().Should().Be("Bearer jwt-outer");
        }
        IncomingBearerCarrier.GetForwardedAuth().Should().BeNull();
    }
}
