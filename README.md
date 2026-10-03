<div align="center">

# Dashboard Metas

### El avance de producción contra la meta, por área y en tiempo real, en la TV de la planta

[![Licencia Apache 2.0](https://img.shields.io/badge/licencia-Apache%202.0-000000)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-VB.NET-000000)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![JD Edwards](https://img.shields.io/badge/JD%20Edwards-AS400%20%2F%20ODBC-C74634)](#cómo-funciona)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)](#requisitos)

Aplicación de escritorio para Windows que consulta **JD Edwards World (AS400)** cada pocos minutos y muestra,
en pantalla completa, cuánto lleva cada área contra su meta diaria y mensual: si va **a tiempo o atrasada a esta
hora del turno**, dónde **cerrará el día al ritmo actual** y cómo va el **acumulado del mes**.

<img src="docs/capturas/principal.png" alt="Detalle de un área: cumplimiento de hoy, ritmo del turno y valor por día" width="860">

</div>

## Por qué

El tablero se armaba con un VBScript que generaba una base de Access con consultas *pass-through* a JDE. Funcionaba,
pero solo decía el porcentaje del día: a las 10 de la mañana «35 %» no dice si se va bien o mal. Tampoco había forma de
ver todas las áreas a la vez ni el avance del mes.

Dashboard Metas hace las mismas consultas en una aplicación propia y agrega lo que faltaba para decidir a tiempo:
la meta esperada a esta hora según el horario de cada turno (incluidos los que pasan la medianoche), la proyección al
cierre, la vista general de todas las áreas y el acumulado del mes contra su meta.

## Qué hace

| | |
| --- | --- |
| **Detalle de un área** | Cumplimiento de hoy, valor del día, falta o sobra para la meta, acumulado semanal, último día cerrado, promedio y días en meta |
| **Ritmo del turno** | «Esperado a esta hora» según el horario del área y etiqueta `A TIEMPO` / `ATRASADO $4K` / `META ALCANZADA` |
| **Proyección** | Dónde cierra el día al ritmo actual (en la tarjeta y como extensión punteada de la barra de hoy) |
| **Gráfica por día** | 5 a 31 días, color por cumplimiento, línea de meta, valor en cada barra y tooltip con el detalle |
| **Acumulado del mes** | Tarjeta con % de la meta mensual y esperado a la fecha, y gráfica del acumulado real contra la meta acumulada con proyección a fin de mes |
| **Vista general** | Todas las áreas en mosaico: cumplimiento, ritmo, proyección, mini-gráfica y mes. Clic para ver el detalle |
| **Turnos por área** | Horario y pausa por área, también turnos que pasan la medianoche (22:00 a 06:00) |
| **Siempre con datos** | Abre con los últimos datos guardados; si una consulta falla, esa área conserva sus datos y lo indica en rojo |
| **Para la TV** | Pantalla completa, escala a cualquier resolución, puntero oculto, cambio automático de área, español e inglés |
| **Sin ventanas inesperadas** | La actualización automática nunca abre diálogos; si falta la contraseña aparece un botón **Conectar** |
| **Datos** | Tabla día por día, copiable a Excel |
| **Diagnóstico** | Proceso de 32 bits, DSN y driver ODBC, y prueba de las tres consultas sin guardar nada |
| **Modo demo** | `--demo` corre todo sin AS400, con datos inventados, fallas simuladas y hora simulada |

## Cómo se ve

### Vista general

<img src="docs/capturas/vista_general.png" alt="Vista general con las seis áreas en mosaico" width="860">

Cada mosaico resume un área: la franja de color es el ritmo (verde a tiempo, naranja atrasado, gris fuera de turno),
la línea vertical en la barra marca cuánto se esperaba a esta hora y la mini-gráfica muestra los últimos días.

### Acumulado del mes

<img src="docs/capturas/mes.png" alt="Acumulado del mes contra la meta acumulada con proyección" width="860">

Un solo eje: el acumulado real contra la meta acumulada (línea recta sobre los días laborables) y, desde hoy, la
proyección a fin de mes al ritmo actual.

### Configuración y datos

<table>
<tr>
<td width="58%"><img src="docs/capturas/configuracion.png" alt="Configuración de áreas, metas y turnos"></td>
<td width="42%"><img src="docs/capturas/datos.png" alt="Tabla de datos por día"></td>
</tr>
<tr>
<td><b>Áreas y metas</b> — nombres, meta diaria y mensual, turno y pausa</td>
<td><b>Datos</b> — el detalle día por día, copiable a Excel</td>
</tr>
</table>

> Las capturas son del **modo demo**: las cifras son inventadas.

## Cómo funciona

```text
cada 5 min ──> una conexión ODBC (DSN de 32 bits, IBM i Access) ──> 3 consultas, primero la del área en pantalla
                 Y1    entregas a ensamble   F31122 + F4801 + F4211
                 Y2…Y7 estaciones            F58C3120 × precio W01 (F41D200)
                 SHP   embarques             dcLINK DCTXF × precio W01
              ──> valor por área y día ──> meta del día, ritmo del turno, proyección, semana y mes ──> pantalla
```

- **El proceso es de 32 bits a propósito.** El DSN de IBM i Access de las PC de planta es de 32 bits.
- **Las consultas van parametrizadas.** Planta, operación, tipo de precio, transacción y fecha viajan como parámetros
  con `CAST`; las bibliotecas no pueden ser parámetro, así que se validan antes de usarse.
- **Si una consulta falla, las otras se actualizan.** El área afectada conserva sus últimos datos y la pantalla dice
  desde qué hora son. Los últimos datos se guardan en disco, así que al abrir la app ya hay números.
- **El ritmo usa el horario de cada turno.** Esperado = meta × parte del turno ya trabajada (sin la pausa).
  JDE registra por fecha calendario, así que un turno de 22:00 a 06:00 aporta a cada día las horas que caen en esa
  fecha (00:00–06:00 y 22:00–24:00).
- **Proyecciones con criterio.** La del día aparece a los 30 min de turno y la del mes desde el tercer día laborable:
  antes saltan demasiado.
- **Una sola escala por gráfica.** La gráfica mensual muestra solo el acumulado (real, meta y proyección), nunca dos ejes.
- **La lógica no sabe de dónde vienen los datos.** El repositorio ODBC y el demo implementan la misma interfaz.

## Tecnologías

| Área | Tecnología |
| --- | --- |
| Aplicación | VB.NET, .NET 10, WPF con MVVM (CommunityToolkit.Mvvm, propiedades escritas a mano) |
| Gráficas | Controles propios dibujados con `DrawingContext` (sin librerías de terceros) |
| Datos | System.Data.Odbc con IBM i Access ODBC Driver |
| Configuración y DI | Microsoft.Extensions.Hosting |
| Logs | Serilog a archivo diario |
| Seguridad | DPAPI (`ProtectedData`, alcance usuario) |
| Pruebas | xUnit en VB, 97 pruebas |
| Publicación | Un solo `.exe` win-x86 self-contained, portable, sin administrador |

## Requisitos

- **Windows 10 (1607+) u 11.** No hace falta instalar .NET: va dentro del ejecutable.
- **Sin permisos de administrador.** Corre desde cualquier carpeta del usuario.
- **DSN ODBC de 32 bits** hacia el AS400 con *IBM i Access Client Solutions* (solo para el modo real).

## Puesta en marcha

Requisito para compilar: **.NET SDK 10** en Windows.

```cmd
git clone https://github.com/esdrasclth/dashboard-metas.git
cd dashboard-metas
dotnet test DashboardMetas.sln
dotnet build DashboardMetas.sln
src\DashboardMetas.App\bin\Debug\net10.0-windows\win-x86\DashboardMetas.exe --demo --ventana
```

### Modo demo

`--demo` usa datos inventados con la misma forma que devuelve JDE: estables día a día, sin domingos, y el valor de hoy
crece durante el turno de cada área (unas van adelantadas y otras atrasadas). La contraseña puede ser cualquiera salvo
**`incorrecta`**, que simula CWBSY0002. En Configuración › Modo demo se puede:

- **Simular la falla** de una de las tres consultas, para ver el estado rojo con los últimos datos conservados.
- **Simular la hora** (`11:20`) o la fecha y hora (`2026-10-20 11:20`), para ver el ritmo, las proyecciones y el
  mes en cualquier momento.

### Publicar

```cmd
publicar.cmd
```

Corre las pruebas y deja en `publish\DashboardMetas` la carpeta portable: `DashboardMetas.exe`, `appsettings.json` y,
si existe en tu copia, `appsettings.empresa.json`. Se copia esa carpeta a la PC de la TV y se abre el `.exe`.

## Uso

| Tecla | Acción |
| --- | --- |
| F5 | Actualizar ahora |
| ← → | Área anterior / siguiente |
| V | Vista general / detalle |
| M | Gráfica por día / acumulado del mes |
| F11 | Pantalla completa sí/no (Esc sale) |

**Contraseña de JDE:** la primera vez se pide y, si se marca «Recordar», se guarda **cifrada con DPAPI** (solo ese
usuario de Windows en esa PC puede leerla). Las actualizaciones automáticas nunca la piden: si falta o venció, el
estado lo dice y aparece **Conectar**. Alternativa: «Usar el inicio de sesión del driver IBM i Access».

## Configuración

`appsettings.json` trae valores genéricos. **Los datos reales de conexión de cada empresa no van en el repositorio**:
se ponen en `src/DashboardMetas.App/appsettings.empresa.json` (ignorado por Git) a partir de
[`appsettings.empresa.example.json`](src/DashboardMetas.App/appsettings.empresa.example.json):

```json
{
  "Jde": {
    "Dsn": "NOMBRE DEL DSN",
    "User": "USUARIO_AS400",
    "Branch": "001",
    "AssemblyLibrary": "SISTEMA.BIBLIOTECA",
    "StationsLibrary": "BIBLIOTECA",
    "PricesLibrary": "BIBLIOTECA",
    "DcLinkLibrary": "BIBLIOTECA"
  }
}
```

| Clave | Uso |
| --- | --- |
| `Jde:Dsn` · `User` | DSN ODBC de 32 bits y usuario del AS400 (la contraseña **nunca** va en archivos de configuración) |
| `Jde:UseDriverSignOn` | `true` = no enviar contraseña: el driver IBM i Access inicia sesión por su cuenta |
| `Jde:Branch` | Planta (MCU) sin relleno; se rellena a 12 caracteres donde JDE lo requiere |
| `Jde:*Library` | Bibliotecas de ensamble (F31122, F4801, F4211), estaciones (F58C3120), precios (F41D200) y dcLINK (DCTXF) |
| `Jde:PriceType` · `AssemblyOperation` · `ShipmentTransaction` | Tipo de precio (W01), operación de entrega (51000) y transacción de embarque (CS) |
| `Jde:StationsQuantityDivisor` · `PriceDivisor` | Divisores por si las cantidades o precios vienen multiplicados (1 = como vienen) |
| `Dashboard:RefreshMinutes` · `ExcludeSundays` · `StartFullScreen` | Cada cuántos minutos consultar, domingos fuera de la gráfica y abrir en pantalla completa |
| `Dashboard:DefaultDailyGoal` · `ShiftStart` · `ShiftEnd` · `BreakStart` · `BreakEnd` | Meta y turno iniciales de cada área (luego se cambian por área en la pantalla) |
| `Dashboard:MinMinutesForProjection` | Minutos de turno antes de proyectar el cierre del día (30) |
| `Demo:Enabled` · `FailingSource` · `SimulatedTime` | Modo demo, consulta que falla a propósito y hora simulada |

Lo que se cambia en **Configuración** se guarda en `%LocalAppData%\DashboardMetas\`: `usersettings.json` (conexión,
tiene prioridad), `preferencias.json` (idioma, días, rotación, vista, y nombre, metas y turno de cada área) y
`ultimos_datos.json`. **Restaurar valores de la empresa** vuelve a los de `appsettings.empresa.json`.

**Meta mensual:** vacía = meta diaria × días laborables del mes (sin domingos si se excluyen). Si los sábados rinden
menos, conviene escribir la meta mensual real.

## Datos

```text
%LocalAppData%\DashboardMetas\
  usersettings.json      conexión editada en Configuración
  preferencias.json      idioma, días, rotación, vista, áreas (nombres, metas, turnos)
  ultimos_datos.json     últimos datos recibidos (ultimos_datos_demo.json en modo demo)
  clave_jde.dat          contraseña cifrada con DPAPI (clave_demo.dat en modo demo)
  logs\                  un log por día, 60 días
```

**`clave_jde.dat` es una credencial**: solo el mismo usuario de Windows en la misma PC puede leerla. Ni la contraseña
ni la cadena de conexión se escriben en los logs.

## Solución de problemas

| Mensaje | Qué hacer |
| --- | --- |
| **Falta la contraseña de JDE** | Presionar **Conectar** y escribirla con «Recordar» marcado |
| **Contraseña incorrecta o vencida (CWBSY0002)** | Se borra la guardada y se vuelve a pedir. Si sigue, cambiarla en el AS400 |
| **No se encontró el origen de datos** (IM002) | El DSN no existe en 32 bits: revisarlo en `C:\Windows\SysWOW64\odbcad32.exe`. **Diagnóstico** dice si está solo en 64 bits |
| **No se pudo cargar el driver** (IM003) | Instalar o reparar IBM i Access Client Solutions (ODBC de 32 bits) |
| **Sin conexión con JDE • datos de las HH:mm** | Red o VPN caída: se reintenta sola en cada actualización y conserva los últimos datos |
| **JDE no devolvió registros de esta área** | La consulta respondió vacía para el rango: revisar planta, bibliotecas y operación en Configuración |
| **Hay piezas pero sin precio W01** | Los artículos no tienen precio en F41D200 para el tipo configurado |
| "Windows protegió su PC" | El ejecutable no está firmado: **Más información → Ejecutar de todas formas** |

## Estructura

```text
src/
  DashboardMetas.Core     modelos, cálculo del día, del turno y del mes, textos ES/EN, sesión y actualizador
  DashboardMetas.Data     consultas JDE, repositorio ODBC (IBM i Access) y repositorio demo, misma interfaz
  DashboardMetas.App      WPF: tablero, vista general, gráficas propias, configuración, DPAPI, diagnóstico
tests/DashboardMetas.Tests  xUnit en VB
docs/capturas/              imágenes de este README
```

## Contribuir

[`CONTRIBUTING.md`](CONTRIBUTING.md) explica cómo probar sin un AS400, qué no romper y qué revisar antes de un
pull request.

## Licencia

**[Apache 2.0](LICENSE)**. Puedes usar, modificar y distribuir el código, incluso comercialmente, conservando el aviso
de copyright y la licencia.

JD Edwards es marca de Oracle; IBM i y AS400 son marcas de IBM; dcLINK es marca de su fabricante. Este proyecto no
está afiliado a ninguno de ellos.
