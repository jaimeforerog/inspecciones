# Review notes — fix-FU-37 (FakeTimeProvider en InspeccionesAppFactory)

**Autor:** orchestrator asumiendo rol `reviewer` (slice transversal de fixture, sin cambios al dominio — análogo a fix-FU-32)
**Fecha:** 2026-05-11
**Veredicto:** **approved**

## 1. Alineación con el spec

| Sección spec | Cumplimiento | Evidencia |
|---|---|---|
| §0 Corrección de causa raíz | ✅ | Auditoría documentada confirma que el bug no era `DateTime.UtcNow` en handlers sino ausencia del swap en fixture. Comentario `InspeccionesAppFactory.cs:121-123` lo recoge inline. |
| §1 Intención | ✅ | `FakeTimeProvider` con `2026-05-08T15:00:00Z` registrado en fixture. |
| §13.1 csproj | ✅ | `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` añadido. |
| §13.2 Fixture | ✅ | Bloque de swap exactamente como spec lo describe — sin `using` faltantes, comportamiento determinístico. |
| §13 Restricciones — "No se toca: producción" | ✅ | `Program.cs`, handlers, dominio sin cambios. Verificado contra `git status`. |
| §14 Resultado esperado | ✅ | 26/32 superados (era 24/32). Coincide con la predicción exacta del spec. |

## 2. Desvío del spec — armonización 1k

El green-notes §2 documenta el desvío: cambio de `CapturadoEn` en `GenerarOTEndpointTests.cs:29` de `14:00Z` a `15:00Z` para alinear con el timestamp canónico del spec FU-37.

**Aprobación:** el desvío fue solicitado y autorizado explícitamente por el usuario antes de aplicarse. El cambio es de 1 dígito en una constante de test que la propia clase usa como única declaración (los 5+ usos derivan de ella). Sin riesgo de inconsistencia.

**Alternativa descartada (correcta):** cambiar el `FakeTimeProvider` a `14:00Z` hubiera roto el slice 1l que ya usaba `15:00Z`. Llevar dos `FakeTimeProvider` por test agregaba complejidad sin justificación. La armonización es la opción más simple y consistente.

## 3. Calidad del código

- Comentario en `InspeccionesAppFactory.cs:115-123` documenta intención, timestamp, FU cerrado, y corrección de causa raíz. Lectura futura no requerirá ir al spec.
- Patrón idiomático consistente con el bloque hermano (`EventLogLoggerProvider` removal).
- Cero warnings, `TreatWarningsAsErrors=true` activo.
- `FakeTimeProvider` no se expone — la fixture es opaca. Si futuros tests necesitan avanzar el reloj, lo harán via `factory.Services.GetRequiredService<TimeProvider>() as FakeTimeProvider`, pero no es necesario hoy.

## 4. Veredicto

**`approved`**

Razones:
- Spec cumplido al 100 % (con desvío autorizado y justificado).
- Cero código de dominio tocado — fix puramente de plumbing.
- Tests del spec verdes (2/2). Suite completa: 26/32 (los 4 fallos remanentes son FU-36/FU-38 preexistentes, explícitamente out-of-scope).
- Cero regresiones. Cero warnings.
- Documentación de causa raíz original corregida en spec §0 (auditoría de handlers + dominio confirmó que cumplían la regla "prohibido DateTime.UtcNow").

## 5. Followups derivados del review

Ninguno. Los rojos preexistentes ya están registrados como FU-36 y FU-38 — no aplican como followup de este slice.

## 6. Próximos pasos del orquestador

1. Commit único: `fix(FU-37): FakeTimeProvider en InspeccionesAppFactory` con HEREDOC + coautoría Claude. Mencionar armonización colateral del test 1k.
2. Actualizar `FOLLOWUPS.md`: marcar FU-37 cerrado con SHA del commit, nota de causa raíz reclasificada.
3. Verificar nota de FU-36 (no requiere actualización — el test sigue rojo por 400 BadRequest, el fix de FU-37 no lo cambia de estado).
4. Detener — no arrancar otro slice. El usuario decide próximo paso.
