using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class DateTests
{
    // ========== Constructor Tests ==========

    [Fact]
    public void Date_NoArgs_CreatesCurrentDate()
    {
        // Date with no args should create a date near current time
        var source = @"
            let d = new Date();
            let now = Date.now();
            let diff = now - d.getTime();
            console.log(diff >= 0 && diff < 1000);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Date_Milliseconds_CreatesFromEpoch()
    {
        // Create date from milliseconds since epoch
        var source = @"
            let d = new Date(0);
            console.log(d.getTime());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Date_Components_CreatesCorrectDate()
    {
        // Create date from year, month, day
        // Note: month is 0-indexed (0 = January)
        var source = @"
            let d = new Date(2024, 0, 15);
            console.log(d.getFullYear());
            console.log(d.getMonth());
            console.log(d.getDate());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2024\n0\n15\n", output);
    }

    [Fact]
    public void Date_Components_WithTime()
    {
        var source = @"
            let d = new Date(2024, 5, 20, 14, 30, 45, 123);
            console.log(d.getFullYear());
            console.log(d.getMonth());
            console.log(d.getDate());
            console.log(d.getHours());
            console.log(d.getMinutes());
            console.log(d.getSeconds());
            console.log(d.getMilliseconds());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2024\n5\n20\n14\n30\n45\n123\n", output);
    }

    [Fact]
    public void Date_ISOString_ParsesCorrectly()
    {
        var source = @"
            let d = new Date('2024-01-15T10:30:00Z');
            console.log(d.toISOString());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2024-01-15T10:30:00.000Z\n", output);
    }

    // ========== Static Method Tests ==========

    [Fact]
    public void Date_Now_ReturnsNumber()
    {
        var source = @"
            let timestamp = Date.now();
            console.log(typeof timestamp);
            console.log(timestamp > 0);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("number\ntrue\n", output);
    }

    [Fact]
    public void Date_FunctionCall_ReturnsString()
    {
        // Date() called without 'new' returns a string
        var source = @"
            let s = Date();
            console.log(typeof s);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("string\n", output);
    }

    // ========== Getter Tests ==========

    [Fact]
    public void Date_GetMonth_Returns0Indexed()
    {
        // January is 0, December is 11
        var source = @"
            let jan = new Date(2024, 0, 1);
            let dec = new Date(2024, 11, 1);
            console.log(jan.getMonth());
            console.log(dec.getMonth());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n11\n", output);
    }

    [Fact]
    public void Date_GetDay_ReturnsCorrectDayOfWeek()
    {
        // 2024-01-01 is Monday (day 1), Sunday is 0
        var source = @"
            let d = new Date(2024, 0, 7);
            console.log(d.getDay());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output); // January 7, 2024 is Sunday
    }

    [Fact]
    public void Date_GetTimezoneOffset_ReturnsNumber()
    {
        var source = @"
            let d = new Date();
            let offset = d.getTimezoneOffset();
            console.log(typeof offset);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("number\n", output);
    }

    // ========== Setter Tests ==========

    [Fact]
    public void Date_SetFullYear_MutatesAndReturnsTimestamp()
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            let result = d.setFullYear(2025);
            console.log(d.getFullYear());
            console.log(typeof result);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2025\nnumber\n", output);
    }

    [Fact]
    public void Date_SetMonth_MutatesDate()
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            d.setMonth(6);
            console.log(d.getMonth());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void Date_SetDate_MutatesDay()
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            d.setDate(20);
            console.log(d.getDate());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Date_SetHours_MutatesTime()
    {
        var source = @"
            let d = new Date(2024, 0, 15, 10, 30);
            d.setHours(15);
            console.log(d.getHours());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void Date_SetTime_SetsFromEpoch()
    {
        var source = @"
            let d = new Date();
            d.setTime(0);
            console.log(d.getTime());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    // ========== Conversion Tests ==========

    [Fact]
    public void Date_ToISOString_ReturnsUTCFormat()
    {
        var source = @"
            let d = new Date('2024-06-15T12:00:00Z');
            let iso = d.toISOString();
            console.log(iso.includes('2024-06-15'));
            console.log(iso.includes('Z'));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Date_ValueOf_ReturnsTimestamp()
    {
        var source = @"
            let d = new Date(0);
            console.log(d.valueOf());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Date_ToString_ReturnsString()
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            let s = d.toString();
            console.log(typeof s);
            console.log(s.includes('2024'));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("string\ntrue\n", output);
    }

    // ========== Edge Cases ==========

    [Fact]
    public void Date_InvalidString_CreatesInvalidDate()
    {
        var source = @"
            let d = new Date('not a date');
            let time = d.getTime();
            console.log(Number.isNaN(time));
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Date_MonthOverflow_RollsOver()
    {
        // Month 12 should roll over to next year
        var source = @"
            let d = new Date(2024, 12, 1);
            console.log(d.getFullYear());
            console.log(d.getMonth());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2025\n0\n", output);
    }

    [Fact]
    public void Date_TwoDigitYear_MapsTo1900s()
    {
        // Years 0-99 in constructor should map to 1900-1999
        var source = @"
            let d = new Date(99, 0, 1);
            console.log(d.getFullYear());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1999\n", output);
    }
}
