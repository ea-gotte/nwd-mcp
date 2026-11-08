using System.Collections.Generic;
using System.Linq;
using Bimwright.Nwd.Shared.Infrastructure;

namespace Bimwright.Nwd.Tests;

public sealed class CsvTableTests
{
    [Fact]
    public void ParsesCommaCsvWithQuotesBomAndCrlf()
    {
        var t = CsvTable.Parse("﻿id,Name,Note\r\n0:1,\"Pipe, 2\"\"\",x\r\n0:2,Duct,\r\n\r\n");
        Assert.Equal(new[] { "id", "Name", "Note" }, t.Headers);
        Assert.Equal(',', t.Delimiter);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(new[] { "0:1", "Pipe, 2\"", "x" }, t.Rows[0]);
        Assert.Equal(new[] { "0:2", "Duct", "" }, t.Rows[1]);
    }

    [Fact]
    public void DetectsSemicolonFromSpanishExcel()
    {
        var t = CsvTable.Parse("A_01_Navis path;B_02_Cod-Tarea-4D;C_05_Disciplina\nEDIF>MOD1>Tubo 1;A1010;MEC\n");
        Assert.Equal(';', t.Delimiter);
        Assert.Equal("B_02_Cod-Tarea-4D", t.Headers[1]);
        Assert.Equal(new[] { "EDIF>MOD1>Tubo 1", "A1010", "MEC" }, t.Rows[0]);
    }

    [Fact]
    public void DetectsTabsAndKeepsMultilineQuotedCells()
    {
        var t = CsvTable.Parse("k\tv\n1\t\"line1\nline2\"\n");
        Assert.Equal('\t', t.Delimiter);
        Assert.Equal("line1\nline2", t.Rows[0][1]);
    }

    [Fact]
    public void ColumnIndexIgnoresCaseAndSpaces()
    {
        var t = CsvTable.Parse("Id , Name\n1,a\n");
        Assert.Equal(0, t.ColumnIndex("id"));
        Assert.Equal(1, t.ColumnIndex(" NAME "));
        Assert.Equal(-1, t.ColumnIndex("other"));
    }

    [Fact]
    public void RejectsEmptyAndDuplicateHeaders()
    {
        Assert.Throws<System.FormatException>(() => CsvTable.Parse(""));
        Assert.Throws<System.FormatException>(() => CsvTable.Parse("a,A\n1,2\n"));
        Assert.Throws<System.FormatException>(() => CsvTable.Parse("a,\"b\n"));
    }
}

public sealed class UserTabTests
{
    [Fact]
    public void FindsOneBasedIndexAmongUserDefinedTabsOnly()
    {
        var attrs = new List<(bool, string, string)>
        {
            (false, "Elemento", "LcOaNode"),
            (true, "Ecoplanta 4D", "Ecoplanta_4D"),
            (false, "Geometría", "LcOaGeometry"),
            (true, "QA", "QA"),
        };
        Assert.Equal(1, UserTab.FindIndex(attrs, "ecoplanta 4d"));
        Assert.Equal(2, UserTab.FindIndex(attrs, "QA"));
        Assert.Equal(1, UserTab.FindIndex(attrs, "Ecoplanta_4D"));
        Assert.Equal(0, UserTab.FindIndex(attrs, "Elemento"));
        Assert.Equal(0, UserTab.FindIndex(attrs, "missing"));
    }

    [Fact]
    public void MergeKeepsOrderOverridesAndAppends()
    {
        var existing = new List<KeyValuePair<string, object?>> { new("A", "1"), new("B", "2") };
        var updates = new List<KeyValuePair<string, object?>> { new("b", "20"), new("C", "3") };
        var merged = UserTab.Merge(existing, updates, replace: false);
        Assert.Equal(new[] { "A=1", "B=20", "C=3" }, merged.Select(kv => kv.Key + "=" + kv.Value));
    }

    [Fact]
    public void MergeRemovesNullValuesAndReplaceDropsTheRest()
    {
        var existing = new List<KeyValuePair<string, object?>> { new("A", "1"), new("B", "2") };
        var merged = UserTab.Merge(existing, new List<KeyValuePair<string, object?>> { new("A", null) }, replace: false);
        Assert.Equal(new[] { "B" }, merged.Select(kv => kv.Key));
        var replaced = UserTab.Merge(existing, new List<KeyValuePair<string, object?>> { new("Z", 9) }, replace: true);
        Assert.Equal(new[] { "Z" }, replaced.Select(kv => kv.Key));
    }

    [Theory]
    [InlineData("Ecoplanta 4D", "Ecoplanta_4D")]
    [InlineData("  QA / Revisión ", "QA_Revision")]
    [InlineData("123", "_123")]
    public void InternalNamesAreSafeIdentifiers(string tab, string expected)
        => Assert.Equal(expected, UserTab.InternalName(tab));
}

public sealed class SqlGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM item")]
    [InlineData("  select name from sqlite_master where type='table';  ")]
    [InlineData("WITH t AS (SELECT 1 AS x) SELECT x FROM t")]
    [InlineData("SELECT 'delete me' AS note")]
    [InlineData("SELECT \"delete\" FROM \"my;table\" -- drop it; later\n")]
    [InlineData("SELECT [update] FROM t /* insert; */ WHERE x = 'a''b;c'")]
    public void AllowsSingleReadOnlyQueries(string sql)
        => Assert.True(SqlGuard.IsReadOnly(sql, out _));

    [Theory]
    [InlineData("DELETE FROM item")]
    [InlineData("SELECT 1; DROP TABLE item")]
    [InlineData("WITH t AS (SELECT 1) DELETE FROM item")]
    [InlineData("PRAGMA user_version = 3")]
    [InlineData("ATTACH DATABASE 'x' AS y")]
    [InlineData("")]
    // Bypass attempts: quotes inside identifiers/comments must not hide a second statement.
    [InlineData("SELECT \"x'\" ; DELETE FROM t; --'")]
    [InlineData("SELECT [x'] ; DROP TABLE t; --'")]
    [InlineData("SELECT `x'` ; DELETE FROM t; --'")]
    [InlineData("SELECT 1 /* ' */ ; DELETE FROM t /* ' */")]
    [InlineData("SELECT 1 -- '\n; DELETE FROM t")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT \"unterminated")]
    [InlineData("SELECT 1 /* unterminated")]
    public void RejectsWritesAndMultipleStatements(string sql)
    {
        Assert.False(SqlGuard.IsReadOnly(sql, out var e));
        Assert.NotEmpty(e);
    }
}
