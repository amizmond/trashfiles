using Estimation.Excel.Models;

namespace Estimation.Core.Models;

public class UploadMaster
{
    [ExcelColumn("Capital Project")]
    public string? CapitalProject { get; set; }

    [ExcelColumn("Program")]
    public string? Program { get; set; }

    [ExcelColumn("Ranking")]
    public int? Ranking { get; set; }

    [ExcelColumn("BO Parent Id")]
    public string? BoParentId { get; set; }

    [ExcelColumn("Unfunded")]
    public string? Unfunded { get; set; }

    [ExcelColumn("BO Parent")]
    public string? BoParent { get; set; }

    [ExcelColumn("Business Outcome")]
    public string? BusinessOutcome { get; set; }

    [ExcelColumn("PI")]
    public string? Pi { get; set; }

    [ExcelColumn("Date expected")]
    public DateTime? DateExpected { get; set; }

    [ExcelColumn("BO Id")]
    public string? BoId { get; set; }

    [ExcelColumn("Key")]
    public string? Key { get; set; }

    [ExcelColumn("priority")]
    public string? Priority { get; set; }

    [ExcelColumn("Summary")]
    public string? Summary { get; set; }

    [ExcelColumn("Team")]
    public string? Team { get; set; }

    [ExcelColumn("Revised BA")]
    public int? RevisedBa { get; set; }

    [ExcelColumn("Revised C#.NET")]
    public int? RevisedCSharpNet { get; set; }

    [ExcelColumn("Revised DB/SSIS")]
    public int? RevisedDbSsis { get; set; }

    [ExcelColumn("Revised R")]
    public int? RevisedR { get; set; }

    [ExcelColumn("Revised GUI")]
    public int? RevisedGui { get; set; }

    [ExcelColumn("Revised DevOps/Env.")]
    public int? RevisedDevOpsEnv { get; set; }

    [ExcelColumn("Qliksense")]
    public int? QlikSense { get; set; }

    [ExcelColumn("Revised Support/testing")]
    public int? RevisedSupportTesting { get; set; }
}


public class UploadLxlTeamData
{
    [ExcelColumn("CIO")]
    public string? Cio { get; set; }

    [ExcelColumn("CIO-1")]
    public string? Cio1 { get; set; }

    [ExcelColumn("CIO-2")]
    public string? Cio2 { get; set; }

    [ExcelColumn("Team")]
    public string? TeamFullName { get; set; }

    [ExcelColumn("Optional team tag")]
    public string? OptionalTeamTag { get; set; }

    [ExcelColumn("Platform")]
    public string? Platform { get; set; }

    [ExcelColumn("Category")]
    public string? Category { get; set; }

    [ExcelColumn("Resource ID")]
    public string? ResourceId { get; set; }

    [ExcelColumn("Resource name")]
    public string? ResourceName { get; set; }

    [ExcelColumn("City")]
    public string? City { get; set; }

    [ExcelColumn("Country")]
    public string? Country { get; set; }

    [ExcelColumn("Employee type")]
    public string? EmployeeType { get; set; }

    [ExcelColumn("Corporate grade")]
    public string? CorporateGrade { get; set; }

    [ExcelColumn("Role")]
    public string? Role { get; set; }

    [ExcelColumn("Valid now")]
    public string? ValidNow { get; set; }

    [ExcelColumn("Feature team")]
    public string? FeatureTeam { get; set; }

    [ExcelColumn("Line manager name (per SAP)")]
    public string? LineManagerNamePerSap { get; set; }

    [ExcelColumn("Vendor")]
    public string? Vendor { get; set; }

    [ExcelColumn("C#")]
    public string? CSharp { get; set; }

    [ExcelColumn("C++")]
    public string? CPlusPlus { get; set; }

    [ExcelColumn("Java")]
    public string? Java { get; set; }

    [ExcelColumn("Python")]
    public string? Python { get; set; }

    [ExcelColumn("\"R\"")]
    public string? R { get; set; }

    [ExcelColumn("SCALA")]
    public string? Scala { get; set; }

    [ExcelColumn("JavaScript")]
    public string? JavaScript { get; set; }

    [ExcelColumn("SQL (MySQL, PostgreSQL, MSSQL)")]
    public string? Sql { get; set; }

    [ExcelColumn("NoSQL (MongoDB, DynamoDB, HBASE etc.)")]
    public string? NoSql { get; set; }

    [ExcelColumn("HADOOP (HIVE/Impala)")]
    public string? HadoopHiveImpala { get; set; }

    [ExcelColumn("AWS")]
    public string? Aws { get; set; }

    [ExcelColumn("Azure")]
    public string? Azure { get; set; }

    [ExcelColumn("Docker")]
    public string? Docker { get; set; }

    [ExcelColumn("Kubernetes")]
    public string? Kubernetes { get; set; }

    [ExcelColumn("CI/CD Pipelines (Jenkins, GitHub Actions)")]
    public string? CiCdPipelines { get; set; }

    [ExcelColumn("React.js")]
    public string? ReactJs { get; set; }

    [ExcelColumn("Angular")]
    public string? Angular { get; set; }

    [ExcelColumn("Qliksense")]
    public string? QlikSense { get; set; }

    [ExcelColumn("HTML/CSS")]
    public string? HtmlCss { get; set; }

    [ExcelColumn(".NET Core")]
    public string? DotNetCore { get; set; }

    [ExcelColumn("Node.js")]
    public string? NodeJs { get; set; }

    [ExcelColumn("Spring Boot")]
    public string? SpringBoot { get; set; }

    [ExcelColumn("Unit Testing (JUnit, NUnit)")]
    public string? UnitTesting { get; set; }

    [ExcelColumn("Test Automation (Selenium, Cypress)")]
    public string? TestAutomation { get; set; }

    [ExcelColumn("API Testing (Postman, REST Assured)")]
    public string? ApiTesting { get; set; }

    [ExcelColumn("Analysis")]
    public string? Analysis { get; set; }

    [ExcelColumn("Support/testing")]
    public string? SupportTesting { get; set; }

    [ExcelColumn("DevOps Calc")]
    public string? DevOpsCalc { get; set; }

    [ExcelColumn("C#.NET Calc")]
    public string? CSharpNetCalc { get; set; }

    [ExcelColumn("R Calc")]
    public string? RCalc { get; set; }

    [ExcelColumn("DB/SSIS Calc")]
    public string? DbSsisCalc { get; set; }

    [ExcelColumn("QS Calc")]
    public string? QsCalc { get; set; }
}


public class UploadFeature
{
    [ExcelColumn("Jira ID", AllowMissing = true)]
    public string? JiraId { get; set; }

    [ExcelColumn("Name")]
    public string? Name { get; set; }

    [ExcelColumn("Summary")]
    public string? Summary { get; set; }

    [ExcelColumn("Description", AllowMissing = true)]
    public string? Description { get; set; }

    [ExcelColumn("Comments", AllowMissing = true)]
    public string? Comments { get; set; }

    [ExcelColumn("Ranking", AllowMissing = true)]
    public int? Ranking { get; set; }

    [ExcelColumn("Date Expected", AllowMissing = true)]
    public DateTime? DateExpected { get; set; }

    [ExcelColumn("Feature Lead", AllowMissing = true)]
    public string? FeatureLead { get; set; }

    [ExcelColumn("Is Linked To Jira", AllowMissing = true)]
    public bool? IsLinkedToTheJira { get; set; }

    [ExcelColumn("Business Outcome", AllowMissing = true)]
    public string? BusinessOutcome { get; set; }

    [ExcelColumn("PI", AllowMissing = true)]
    public string? Pi { get; set; }

    [ExcelColumn("Unfunded Option", AllowMissing = true)]
    public string? UnfundedOption { get; set; }
}
