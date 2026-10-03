# Contribuir a Dashboard Metas

Dashboard Metas es software libre bajo [Apache 2.0](LICENSE). Los reportes de fallos y las propuestas son
bienvenidos. Si vas a cambiar algo grande, abre antes un issue para comentarlo.

---

## Entorno

Requisitos: **Windows** y **.NET SDK 10**. No hace falta un AS400 ni el driver ODBC.

```cmd
dotnet build DashboardMetas.sln
dotnet test DashboardMetas.sln
src\DashboardMetas.App\bin\Debug\net10.0-windows\win-x86\DashboardMetas.exe --demo --ventana
```

El **modo demo** recorre toda la aplicación con datos inventados: detalle, vista general, gráfica del mes,
configuración, datos y diagnóstico. La contraseña `incorrecta` simula CWBSY0002. En Configuración › Modo demo
puedes simular la falla de una consulta y la hora o la fecha (`11:20`, `2026-10-20 11:20`) para ver el ritmo del
turno, las proyecciones y el mes en cualquier momento.

`dotnet test` no recompila la App: corre `dotnet build` antes de abrirla.

La app compila como **x86** a propósito: el DSN de IBM i Access que se usa en producción es de 32 bits.
Se compila *self-contained*, así que no necesitas el runtime de .NET de 32 bits instalado.

---

## Pruebas

```cmd
dotnet test DashboardMetas.sln
```

Las pruebas son de xUnit, en VB, y no tocan ODBC ni la interfaz: usan el repositorio demo y dobles en memoria
(`tests/DashboardMetas.Tests/TestDoubles.vb`). Cubren las consultas (parámetros y bibliotecas), el cálculo del día
(último día cerrado, semana, eje), los turnos (pausas y medianoche), el ritmo y la proyección, el mes, los formatos,
las preferencias, la apertura de sesión y el actualizador (fallas parciales, caché).

Si cambias un cálculo, agrega la prueba con fechas fijas (no `Date.Now`) y comenta el caso en la prueba.

---

## Qué no romper

- **Nada de la empresa en el repositorio.** DSN, usuario, sistema, planta y bibliotecas reales van en
  `appsettings.empresa.json`, que está en `.gitignore`; la versión anterior y los datos reales (`referencia/`),
  también. Los datos del demo y de las pruebas son inventados. Revisa tu `git diff` antes de cada commit.
- **La contraseña nunca se escribe** en configuración, logs ni mensajes. Tampoco la cadena de conexión.
- **Las consultas van parametrizadas.** Nunca concatenes planta, fechas ni códigos en el SQL; las bibliotecas solo
  entran después de `JdeSettings.IsValidLibrary`.
- **La actualización automática nunca abre diálogos.** La pantalla puede estar en una TV sin nadie enfrente.
- **Una consulta fallida no borra datos.** El área conserva lo último que respondió JDE.
- **Una sola escala por gráfica.** Nada de dos ejes Y.
- **Formato independiente de la región**: montos y fechas de la pantalla usan `InvariantCulture` y textos propios.
- **La UI nunca se congela**: E/S asíncrona con `CancellationToken`.

---

## Estilo

- `Option Strict On`, `Option Explicit On` y `Option Infer On` en todos los proyectos.
- **Código en inglés** (clases, métodos, variables, comentarios); **interfaz, mensajes y commits en español**.
  La pantalla principal también tiene inglés: todo texto visible pasa por `Texts.L(es, en)`.
- MVVM: nada de lógica en el code-behind salvo lo estrictamente visual.
- CommunityToolkit.Mvvm solo por sus clases base: sus generadores no funcionan en VB, así que las propiedades se
  escriben a mano.
- Ojo con VB: no distingue mayúsculas (una variable `tone` choca con el tipo `Tone`, `areas` con la propiedad
  `Areas`) y `Short`, `Alias` o `Exit` son palabras reservadas.
- Mensajes de commit en imperativo y con el porqué: «Proyectar el mes desde el tercer día laborable».

---

## Pull requests

1. Crea una rama desde `main`.
2. `dotnet build` sin advertencias y `dotnet test` en verde.
3. Prueba el cambio en modo demo y, si toca la pantalla, adjunta una captura.
4. Describe qué cambia y por qué.
