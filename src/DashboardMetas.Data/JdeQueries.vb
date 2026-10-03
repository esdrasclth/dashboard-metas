Imports System.Data.Odbc
Imports DashboardMetas.Core.Configuration
Imports DashboardMetas.Core.Models

''' <summary>A positional ODBC parameter (the IBM i uses "?" markers, in text order).</summary>
Public NotInheritable Class QueryParameter
    Public Sub New(name As String, type As OdbcType, value As Object, Optional size As Integer = 0)
        Me.Name = name
        Me.Type = type
        Me.Value = value
        Me.Size = size
    End Sub
    Public ReadOnly Property Name As String
    Public ReadOnly Property Type As OdbcType
    Public ReadOnly Property Value As Object
    Public ReadOnly Property Size As Integer
End Class

Public NotInheritable Class JdeQuery
    Public Sub New(sql As String, parameters As IReadOnlyList(Of QueryParameter))
        Me.Sql = sql
        Me.Parameters = parameters
    End Sub
    Public ReadOnly Property Sql As String
    Public ReadOnly Property Parameters As IReadOnlyList(Of QueryParameter)
End Class

''' <summary>
''' The three queries of Crear_Dashboard_Avance_Areas.vbs (same joins, filters and arithmetic). All of them
''' return AREA, FECHA, PIEZAS, COSTO_TOTAL, ESTILOS, one row per area and day.
''' Plant, operation, price type, transaction and start date travel as parameters (CAST so the IBM i knows
''' their type); libraries cannot be parameters, so they are validated before being put in the text.
''' Station quantities and prices come undivided: the configurable divisors are applied in .NET.
''' </summary>
Public NotInheritable Class JdeQueries

    Private Sub New()
    End Sub

    ''' <summary>JDE julian date CYYDDD (2026-10-02 → 126275).</summary>
    Public Shared Function ToJulian(d As Date) As Integer
        Return (d.Year - 1900) * 1000 + d.DayOfYear
    End Function

    ''' <summary>dcLINK date YYYYMMDD.</summary>
    Public Shared Function ToYmd(d As Date) As Integer
        Return d.Year * 10000 + d.Month * 100 + d.Day
    End Function

    Public Shared Function Build(source As ProductionSource, settings As JdeSettings, fromDate As Date) As JdeQuery
        Select Case source
            Case ProductionSource.AssemblyDeliveries : Return Assembly(settings, fromDate)
            Case ProductionSource.StationActivity : Return Stations(settings, fromDate)
            Case ProductionSource.Shipments : Return Shipments(settings, fromDate)
            Case Else : Throw New ArgumentOutOfRangeException(NameOf(source))
        End Select
    End Function

    Private Shared Function Library(value As String, what As String) As String
        If Not JdeSettings.IsValidLibrary(value) Then Throw New ArgumentException($"Biblioteca de {what} no válida: '{value}'.")
        Return value.Trim()
    End Function

    Private Shared Function PriceCte(settings As JdeSettings) As String
        Return $"P AS (SELECT PMLITM, MAX(PM@RG$) AS RG FROM {Library(settings.PricesLibrary, "precios")}.F41D200" &
               " WHERE PM@RTY = CAST(? AS VARCHAR(10)) GROUP BY PMLITM)"
    End Function

    ''' <summary>Y1 - Deliveries to Assembly: MAX unit cost per style × SUM of pieces.</summary>
    Public Shared Function Assembly(settings As JdeSettings, fromDate As Date) As JdeQuery
        Dim lib1 = Library(settings.AssemblyLibrary, "ensamble (Y1)")
        Dim sql =
            "WITH DETALLE AS (" &
            " SELECT TRIM(F4801.WALITM) AS STYLE," &
            "  WT.WTDGL AS DGL," &
            $"  CAST((SELECT MAX(F4211.SDLPRC) FROM {lib1}.F4211 F4211" &
            "        WHERE F4211.SDRORN = DIGITS(F4801.WADOCO)) / 10000 AS DECIMAL(15, 2)) AS COSTO_UNITARIO," &
            "  CAST(WT.WTSOQS / 100 AS DECIMAL(15, 2)) AS PIEZAS" &
            $" FROM {lib1}.F31122 WT" &
            $" INNER JOIN {lib1}.F4801 F4801 ON F4801.WADOCO = WT.WTDOCO" &
            " WHERE TRIM(WT.WTMMCU) = CAST(? AS VARCHAR(12))" &
            "  AND WT.WTOPSQ = CAST(? AS DECIMAL(15, 0))" &
            "  AND WT.WTSOQS <> 0" &
            "  AND UPPER(WT.WTKITL) LIKE '%FIN%'" &
            "  AND F4801.WAMCU = CAST(? AS CHAR(12))" &
            "  AND F4801.WASRST <> '98'" &
            "  AND WT.WTDGL >= CAST(? AS DECIMAL(7, 0))" &
            "), POR_ESTILO AS (" &
            " SELECT DGL, STYLE, MAX(COSTO_UNITARIO) AS COSTO_UNITARIO, SUM(PIEZAS) AS PIEZAS" &
            " FROM DETALLE GROUP BY DGL, STYLE" &
            ")" &
            " SELECT 'Y1' AS AREA, DATE(DIGITS(DEC(DGL + 1900000, 7, 0))) AS FECHA," &
            "  SUM(PIEZAS) AS PIEZAS," &
            "  SUM(COALESCE(COSTO_UNITARIO, 0) * PIEZAS) AS COSTO_TOTAL," &
            "  COUNT(*) AS ESTILOS" &
            " FROM POR_ESTILO" &
            " GROUP BY DGL" &
            " ORDER BY DGL"
        Return New JdeQuery(sql, {
            New QueryParameter("branch", OdbcType.VarChar, settings.Branch.Trim(), 12),
            New QueryParameter("operation", OdbcType.Decimal, CDec(settings.AssemblyOperation)),
            New QueryParameter("branchPadded", OdbcType.Char, JdeSettings.PadBranch(settings.Branch), 12),
            New QueryParameter("fromJulian", OdbcType.Decimal, CDec(ToJulian(fromDate)))})
    End Function

    ''' <summary>
    ''' Y2, Y3, Y5, Y7 - Station Activity (F58C3120): quantity per station × W01 price of the base style + 'FIN'.
    ''' One row per area and day.
    ''' </summary>
    Public Shared Function Stations(settings As JdeSettings, fromDate As Date) As JdeQuery
        Dim stationsLib = Library(settings.StationsLibrary, "estaciones")
        Dim columns = AreaCatalog.Stations.Select(Function(s, i) (s.Code, s.Column, Tag:="Q" & (i + 1).ToString(Globalization.CultureInfo.InvariantCulture))).ToList()

        Dim sql As New Text.StringBuilder()
        sql.Append("WITH ").Append(PriceCte(settings)).Append(", ")
        sql.Append("B AS (SELECT S.SRTRDJ AS DJ, TRIM(SUBSTR(S.SRLITM, 1, 9)) AS BASE,")
        sql.Append(" CAST(COALESCE(P.RG, 0) AS DECIMAL(17, 4)) AS RG")
        For Each c In columns
            sql.Append($", S.{c.Column} AS {c.Tag}")
        Next
        sql.Append($" FROM {stationsLib}.F58C3120 S")
        sql.Append(" LEFT JOIN P ON P.PMLITM = SUBSTR(S.SRLITM, 1, 9) || 'FIN'")
        sql.Append(" WHERE TRIM(S.SRMCU) = CAST(? AS VARCHAR(12))")
        sql.Append(" AND S.SRTRDJ >= CAST(? AS DECIMAL(7, 0))), ")
        sql.Append("E AS (SELECT DJ, BASE, MAX(RG) AS RG")
        For Each c In columns
            sql.Append($", CAST(SUM({c.Tag}) AS DECIMAL(15, 2)) AS {c.Tag}")
        Next
        sql.Append(" FROM B GROUP BY DJ, BASE)")
        For i = 0 To columns.Count - 1
            Dim c = columns(i)
            If i > 0 Then sql.Append(" UNION ALL")
            sql.Append($" SELECT '{c.Code}' AS AREA, DATE(DIGITS(DEC(DJ + 1900000, 7, 0))) AS FECHA,")
            sql.Append($" CAST(SUM({c.Tag}) AS DECIMAL(15, 2)) AS PIEZAS,")
            sql.Append($" CAST(SUM({c.Tag} * RG) AS DECIMAL(17, 2)) AS COSTO_TOTAL,")
            sql.Append($" SUM(CASE WHEN {c.Tag} <> 0 THEN 1 ELSE 0 END) AS ESTILOS")
            sql.Append($" FROM E GROUP BY DJ HAVING SUM({c.Tag}) <> 0")
        Next

        Return New JdeQuery(sql.ToString(), {
            New QueryParameter("priceType", OdbcType.VarChar, settings.PriceType.Trim(), 10),
            New QueryParameter("branch", OdbcType.VarChar, settings.Branch.Trim(), 12),
            New QueryParameter("fromJulian", OdbcType.Decimal, CDec(ToJulian(fromDate)))})
    End Function

    ''' <summary>
    ''' SHP - Confirmed shipments (dcLINK CS). W01 price: first by exact item; if missing, by base style (9) + 'FIN'.
    ''' The price is looked up AFTER grouping by item. DB2 note: SUM() returns DECIMAL(31,0); dividing it by
    ''' 100.0 gives a negative scale (SQL0419), so it is first cast to DECIMAL(17,2) and then divided by 100.
    ''' </summary>
    Public Shared Function Shipments(settings As JdeSettings, fromDate As Date) As JdeQuery
        Dim dcLib = Library(settings.DcLinkLibrary, "dcLINK")
        Const d = "DIGITS(DEC(D, 8, 0))"
        Dim sql =
            "WITH " & PriceCte(settings) & ", " &
            "X AS (SELECT T.TXSTDT AS D, TRIM(T.TXITM1) AS ITM," &
            " CAST(CAST(SUM(T.TXQTY1) AS DECIMAL(17, 2)) / 100 AS DECIMAL(15, 2)) AS Q" &
            $" FROM {dcLib}.DCTXF T" &
            " WHERE T.TXTXID = CAST(? AS VARCHAR(10)) AND TRIM(T.TXFACT) = CAST(? AS VARCHAR(12))" &
            " AND T.TXSTDT >= CAST(? AS DECIMAL(8, 0))" &
            " GROUP BY T.TXSTDT, T.TXITM1), " &
            "Y AS (SELECT X.D, X.ITM, X.Q," &
            " CAST(COALESCE(P1.RG, P2.RG, 0) AS DECIMAL(17, 4)) AS RG" &
            " FROM X" &
            " LEFT JOIN P P1 ON P1.PMLITM = X.ITM" &
            " LEFT JOIN P P2 ON P2.PMLITM = LEFT(X.ITM, 9) || 'FIN')" &
            $" SELECT 'SHP' AS AREA, DATE(SUBSTR({d}, 1, 4) || '-' || SUBSTR({d}, 5, 2) || '-' || SUBSTR({d}, 7, 2)) AS FECHA," &
            " CAST(SUM(Q) AS DECIMAL(15, 2)) AS PIEZAS," &
            " CAST(SUM(Q * RG) AS DECIMAL(17, 2)) AS COSTO_TOTAL," &
            " COUNT(*) AS ESTILOS" &
            " FROM Y GROUP BY D"
        Return New JdeQuery(sql, {
            New QueryParameter("priceType", OdbcType.VarChar, settings.PriceType.Trim(), 10),
            New QueryParameter("transaction", OdbcType.VarChar, settings.ShipmentTransaction.Trim(), 10),
            New QueryParameter("branch", OdbcType.VarChar, settings.Branch.Trim(), 12),
            New QueryParameter("fromYmd", OdbcType.Decimal, CDec(ToYmd(fromDate)))})
    End Function

    ''' <summary>Applies the configurable divisors (the SQL returns stations and prices undivided).</summary>
    Public Shared Function Scale(source As ProductionSource, row As DailyProduction, settings As JdeSettings) As DailyProduction
        Dim qtyDiv = If(source = ProductionSource.StationActivity AndAlso settings.StationsQuantityDivisor > 0D, settings.StationsQuantityDivisor, 1D)
        Dim priceDiv = If(source <> ProductionSource.AssemblyDeliveries AndAlso settings.PriceDivisor > 0D, settings.PriceDivisor, 1D)
        If qtyDiv = 1D AndAlso priceDiv = 1D Then Return row
        row.Pieces = Math.Round(row.Pieces / qtyDiv, 2)
        row.Value = Math.Round(row.Value / (qtyDiv * priceDiv), 2)
        Return row
    End Function

End Class
