using DynamicExcel.Write;

namespace DynamicExcel.ConsoleTest;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine(DateTime.Now.ToLongTimeString());

        var dropdownFactory = new ExcelDropdownsFactory<ExcelTestModelShort>();

        dropdownFactory
            .Create("Categories", new List<object> { "Electronics", "Accessories", "Software", "Hardware" })
            .Bind(x => x.Category)
            .Build();

        dropdownFactory
            .Create("Statuses", new List<object> { "Active", "Inactive", "Pending", "Archived" })
            .Bind(x => x.Status)
            .Build();

        dropdownFactory
            .Create("Statuses ver 2", new List<object> { "Not Active", "Not Inactive"})
            .Bind(x => x.Status)
            .Build();


        dropdownFactory
            .Create("numbers", new List<object> { 1, 2, 3, 4 })
            .Bind(x => x.NumberInt)
            .Build();

        var conditionalFactory = new ExcelConditionalRulesFactory<ExcelTestModelShort>();

        conditionalFactory
            .When(p => p.Category)
                .Equals("Electronics")
                .Then(p => p.Location)
                .ChangeColor("#E9E3DF")
                .ChangeReadOnly(true)
            .Build();

        conditionalFactory
            .When(p => p.Category)
                .Equals("Accessories")
                .Then(p => p.Location)
                .ChangeColor("#93DA97")
                .ChangeReadOnly(false)
            .Build();

        conditionalFactory
            .When(p => p.Status)
                .Equals("Inactive")
                .Then(p => p.Status)
                .ChangeColor("#B12C00")
                .ChangeFontColor("#E9E3DF")
                .ChangeFontBold(true)
            .Build();

        conditionalFactory
            .When(p => p.Status)
                .Equals("Active")
                .Then(p => p.Status)
                .ChangeColor("#78C841")
                .ChangeFontColor("#FEFFC4")
                .ChangeFontBold(true)
            .Build();

        conditionalFactory
            .When(p => p.Price)
                .GreaterThan("1000")
                .Then(p => p.Price)
                .ChangeColor("#00FF00")
                .ChangeFontBold(true)
            .Build();

        conditionalFactory
            .When(p => p.Category)
            .Equals("Accessories")
            .Then(p => p.Status)
            .ChangeDropdown("Statuses ver 2")
            .Build();

        //var data = GetListOfRandomData(100);

        //var memoryStream = new ExcelFileBuilder()
        //    .AddSheet<ExcelTestModelShort>("Data")
        //        .WithData(data)
        //        .WithDropdownData(dropdownFactory.DropdownsList, "DropDownsData")
        //        .WithConditionalRules(conditionalFactory)
        //        .Done()
        //    .Build();


        var simple = GetSimpleData();

        var memoryStream = new ExcelFileBuilder()
            .AddSheet<ExcelTestModelShort>("Adjustments 1")
                .WithData(GetListOfRandomData(1_000))
                .WithDropdownData(dropdownFactory.DropdownsList, "Static for Adjustments 1")
                .ExcludeProperties(p => p.Error)
                .Done()
            .AddSheet<ExcelTestModelShort>("Adjustments 3")
                .WithData(GetListOfRandomData(50))
                .WithDropdownData(dropdownFactory.DropdownsList, "DropDownsData")
                .WithConditionalRules(conditionalFactory)
                .Done()
            .AddSheet<ExcelTestModelA>("Empty")
                .Done()
            .AddSimpleSheet(simple.users)
            .AddSimpleSheet(simple.vehicles)
            .AddSimpleSheet(simple.books, "Book")
            .Build();


        memoryStream.Position = 0;

        await using (var fileStream = new FileStream("D:\\excelTestFiles\\SaxApproachStreamMemoryBuilder.xlsx", FileMode.Create, FileAccess.Write))
        {
            await memoryStream.CopyToAsync(fileStream);
        }

        Console.WriteLine("Excel file created successfully");
        Console.WriteLine(DateTime.Now.ToLongTimeString());

        Console.ReadLine();
    }

    private static List<ExcelTestModelShort> GetListOfRandomData(int count)
    {
        var random = new Random();
        var categories = new[] { "Electronics", "Accessories", "Software", "Hardware" };
        var statuses = new[] { "Active", "Inactive", "Pending", "Archived" };
        var locations = new[] { "Warehouse A", "Warehouse B", "Store 1", "Store 2", "Online" };
        var productPrefixes = new[] { "Basic", "Pro", "Enterprise", "Standard" };
        var productTypes = new[] { "Widget", "Gadget", "Tool", "Device" };

        var data = new List<ExcelTestModelShort>();

        for (int i = 0; i < count; i++)
        {
            data.Add(new ExcelTestModelShort
            {
                Status = statuses[random.Next(statuses.Length)],
                TestIntValue = random.Next(1, 100),
                TestLongValue = random.Next(1000, 10000),
                Category = categories[random.Next(categories.Length)],
                Location = locations[random.Next(locations.Length)],
                Price = (decimal)(random.NextDouble() * 2000),
                ProductName = $"{productPrefixes[random.Next(productPrefixes.Length)]} {productTypes[random.Next(productTypes.Length)]}"
            });
        }

        return data;
    }

    public static (List<User> users, List<VehicleDto> vehicles, List<Book> books) GetSimpleData()
    {
        var users = new List<User>();
        for (var i = 0; i < 3; i++)
        {
            users.Add(new User());
        }

        var vehicles = new List<VehicleDto>();
        for (var i = 0; i < 15; i++)
        {
            vehicles.Add(new VehicleDto());
        }

        var books = new List<Book>();
        for (var i = 0; i < 100; i++)
        {
            books.Add(new Book());
        }

        return (users, vehicles, books);
    }
}

// Models =======================================================================================================

public class ExcelTestModelA
{
    [ExcelColumn("Status", 99)]
    public string Status { get; set; }

    [ExcelColumn("IntValue", 1, Color = "#0066CC")]
    public int TestIntValue { get; set; }

    [ExcelColumn("LongValue", 2, Color = "#00A86B")]
    public long TestLongValue { get; set; }

    [ExcelColumn(OrderId = 3)]
    public string Category { get; set; }

    [ExcelColumn(Color = "FFD700")]
    public string CategoryB { get; set; }

    [ExcelColumn("FloatValue", 3, IsReadOnly = true)]
    public float TestFloatValue { get; set; }

    [ExcelColumn("DoubleValue", 4)]
    public double TestDoubleValue { get; set; }

    [ExcelColumn("DecimalValue", 5)]
    public decimal TestDecimalValue { get; set; }

    [ExcelColumn("StringValue", 6)]
    public string? TestStringValue { get; set; }

    [ExcelColumn]
    public char TestCharValue { get; set; }

    [ExcelColumn("BoolValue", 8)]
    public bool TestBoolValue { get; set; }

    [ExcelColumn]
    public DateTime TestDateTimeValue { get; set; }

    [ExcelColumn]
    public TimeSpan TestTimeSpanValue { get; set; }

    [ExcelColumn("GuidValue", 11)]
    public Guid TestGuidValue { get; set; }

    [ExcelColumn("NullableInt", 12)]
    public int? TestNullableInt { get; set; }

    [ExcelColumn("NullableBool", 13)]
    public bool? TestNullableBool { get; set; }

    [ExcelColumn("NullableDate", 14)]
    public DateTime? TestNullableDate { get; set; }
}

public class ExcelTestModelShort
{
    [ExcelColumn("Status", 5)]
    public string? Status { get; set; }

    [ExcelColumn("IntValue", 3, Color = "#FDF5AA")]
    public int TestIntValue { get; set; }

    [ExcelColumn(OrderId = 4, Color = "#FFBC4C")]
    public long TestLongValue { get; set; }

    [ExcelColumn(OrderId = 1)]
    public string? Category { get; set; }

    [ExcelColumn(OrderId = 2)]
    public string? Location { get; set; }

    [ExcelColumn(OrderId = 6)]
    public decimal Price { get; set; }

    [ExcelColumn(OrderId = 7)]
    public string? ProductName { get; set; }

    [ExcelColumn("int", 8)]
    public int? NumberInt { get; set; }

    [ExcelColumn(OrderId = 999, IsReadOnly = true)]
    public string? Error { get; set; }
}

public class User
{
    public int UserId { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime RegisteredAt { get; set; }

    public User()
    {
        var rnd = new Random();
        UserId = rnd.Next(1000, 9999);
        Username = "User_" + UserId;
        Email = $"user{UserId}@example.com";
        IsAdmin = rnd.Next(0, 2) == 1;
        RegisteredAt = DateTime.Now.AddDays(-rnd.Next(1, 365));
    }
}

public class VehicleDto
{
    public string VIN { get; set; }
    public string Model { get; set; }
    public string Brand { get; set; }
    public int Year { get; set; }
    public double Mileage { get; set; }
    public bool IsElectric { get; set; }

    public VehicleDto()
    {
        var rnd = new Random();
        VIN = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
        Model = "Model_" + rnd.Next(100, 999);
        Brand = "Brand_" + rnd.Next(1, 5);
        Year = rnd.Next(2000, 2025);
        Mileage = Math.Round(rnd.NextDouble() * 150000, 2);
        IsElectric = rnd.Next(0, 2) == 1;
    }
}

public class Book
{
    public string ISBN { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
    public int Pages { get; set; }
    public double Rating { get; set; }
    public DateTime PublishedDate { get; set; }
    public bool IsHardcover { get; set; }

    public Book()
    {
        var rnd = new Random();
        ISBN = "978-" + rnd.Next(100000000, 999999999);
        Title = "Book_" + rnd.Next(1, 1000);
        Author = "Author_" + rnd.Next(1, 100);
        Pages = rnd.Next(100, 1000);
        Rating = Math.Round(rnd.NextDouble() * 5, 2);
        PublishedDate = DateTime.Now.AddYears(-rnd.Next(1, 20));
        IsHardcover = rnd.Next(0, 2) == 1;
    }
}