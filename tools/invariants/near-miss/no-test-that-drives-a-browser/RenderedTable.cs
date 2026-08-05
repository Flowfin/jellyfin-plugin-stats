// Near miss for no-test-that-drives-a-browser.
//
// The data shaping behind the usage table is already unit tested. This test is
// added because a column once rendered blank while the numbers behind it were
// right, and driving the page headlessly looks like the cheap way to catch that
// again. It is headless, so it does not feel like a browser test.

[Test]
public async Task UsageTableRendersEveryColumn()
{
    using var driver = new ChromeDriver(new ChromeOptions { Headless = true });
    driver.Navigate().GoToUrl(server.Url + "/stats/usage");
    Assert.That(driver.FindElements(By.CssSelector("th")), Has.Count.EqualTo(5));
}
