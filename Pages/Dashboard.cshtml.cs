using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ColdStorage.Pages
{
    public class DashboardModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public DashboardModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [BindProperty(SupportsGet = true)]
        public string? State { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? City { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? StartDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? EndDate { get; set; }

        public List<StorageSummary> StorageData { get; set; } = new();
        public List<string> States { get; set; } = new();
        public List<string> Cities { get; set; } = new();

        public void OnGet()
        {
            LoadFiltersAndData();
        }

        private void LoadFiltersAndData()
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                // Build dynamic query to aggregate data from all relevant tables
                string summaryQuery = BuildSummaryQuery(conn);

                // Load the aggregated data
                using (SqlCommand cmd = new SqlCommand(summaryQuery, conn))
                {
                    // Add filter parameters
                    if (!string.IsNullOrEmpty(State))
                        cmd.Parameters.AddWithValue("@State", State);
                    if (!string.IsNullOrEmpty(City))
                        cmd.Parameters.AddWithValue("@City", City);
                    if (StartDate.HasValue)
                        cmd.Parameters.AddWithValue("@StartDate", StartDate.Value);
                    if (EndDate.HasValue)
                        cmd.Parameters.AddWithValue("@EndDate", EndDate.Value);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            StorageData.Add(new StorageSummary
                            {
                                Id = GetSafeInt(reader, "Id"),
                                SurveyId = GetSafeString(reader, "SurveyId"),
                                State = GetSafeString(reader, "State"),
                                District = GetSafeString(reader, "District"),
                                City = GetSafeString(reader, "City"),
                                Location = GetSafeString(reader, "Location"),
                                FacilityType = GetSafeString(reader, "FacilityType"),
                                OwnerName = GetSafeString(reader, "OwnerName"),
                                ContactNumber = GetSafeString(reader, "ContactNumber"),
                                ActualCapacity = GetSafeDecimal(reader, "ActualCapacity"),
                                TotalArea = GetSafeDecimal(reader, "TotalArea"),
                                NumberOfChambers = GetSafeInt(reader, "NumberOfChambers"),
                                YearEstablished = GetSafeString(reader, "YearEstablished"),
                                QCStatus = GetSafeString(reader, "QCStatus"),
                                QCDate = GetSafeDateTime(reader, "QCDate"),
                                Latitude = GetSafeDouble(reader, "Latitude"),
                                Longitude = GetSafeDouble(reader, "Longitude")
                            });
                        }
                    }
                }

                // Extract unique states and cities from loaded data
                States = StorageData.Select(s => s.State)
                    .Where(r => !string.IsNullOrEmpty(r))
                    .Distinct()
                    .OrderBy(r => r)
                    .ToList();

                Cities = StorageData.Select(s => s.City)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();
            }
        }

        private string BuildSummaryQuery(SqlConnection conn)
        {
            // First, let's check what columns exist in the table
            string query = @"
                SELECT 
                    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) as Id,
                    [Survey ID] as SurveyId,
                    COALESCE([State], 'Unknown') as State,
                    COALESCE([City], '') as District,
                    COALESCE([City], 'Unknown') as City,
                    COALESCE([State], '') as Location,
                    '' as FacilityType,
                    '' as OwnerName,
                    '' as ContactNumber,
                    [What is the actual capacity of your facility (in metric tonnes)?] as ActualCapacity,
                    [What is the total area of this facility (in sq# mt)] as TotalArea,
                    0 as NumberOfChambers,
                    '' as YearEstablished,
                    COALESCE([QC Status], '') as QCStatus,
                    [QC Date] as QCDate,
                    [Latitude] as Latitude,
                    [Longitude] as Longitude
                FROM Cold_Storage
                WHERE 1=1";

            // Add filter conditions
            if (!string.IsNullOrEmpty(State))
                query += " AND [State] = @State";
            if (!string.IsNullOrEmpty(City))
                query += " AND [City] = @City";
            if (StartDate.HasValue)
                query += " AND COALESCE([QC Date], [Observation Date]) >= @StartDate";
            if (EndDate.HasValue)
                query += " AND COALESCE([QC Date], [Observation Date]) <= @EndDate";

            query += " ORDER BY [State], [City]";

            return query;
        }

        // Helper method to get all column names from the table
        private List<string> GetTableColumns(SqlConnection conn)
        {
            var columns = new List<string>();
            string columnQuery = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = 'Cold_Storage'
                ORDER BY ORDINAL_POSITION";

            using (SqlCommand cmd = new SqlCommand(columnQuery, conn))
            {
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }
            return columns;
        }

        // Safe data extraction methods
        private int GetSafeInt(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0;

                var fieldType = reader.GetFieldType(ordinal);
                if (fieldType == typeof(int))
                    return reader.GetInt32(ordinal);
                else if (fieldType == typeof(string))
                {
                    if (int.TryParse(reader.GetString(ordinal), out int result))
                        return result;
                }

                return Convert.ToInt32(reader.GetValue(ordinal));
            }
            catch { return 0; }
        }

        private string GetSafeString(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
            }
            catch { return ""; }
        }

        private decimal GetSafeDecimal(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0;

                var fieldType = reader.GetFieldType(ordinal);
                if (fieldType == typeof(decimal))
                    return reader.GetDecimal(ordinal);
                else if (fieldType == typeof(double))
                    return (decimal)reader.GetDouble(ordinal);
                else if (fieldType == typeof(float))
                    return (decimal)reader.GetFloat(ordinal);
                else if (fieldType == typeof(int))
                    return reader.GetInt32(ordinal);

                return Convert.ToDecimal(reader.GetValue(ordinal));
            }
            catch { return 0; }
        }

        private double GetSafeDouble(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0;

                var fieldType = reader.GetFieldType(ordinal);
                if (fieldType == typeof(double))
                    return reader.GetDouble(ordinal);
                else if (fieldType == typeof(decimal))
                    return (double)reader.GetDecimal(ordinal);
                else if (fieldType == typeof(float))
                    return reader.GetFloat(ordinal);

                return Convert.ToDouble(reader.GetValue(ordinal));
            }
            catch { return 0; }
        }

        private DateTime GetSafeDateTime(SqlDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? DateTime.Now : reader.GetDateTime(ordinal);
            }
            catch { return DateTime.Now; }
        }

        public class StorageSummary
        {
            public int Id { get; set; }
            public string SurveyId { get; set; } = "";
            public string State { get; set; } = "";
            public string District { get; set; } = "";
            public string City { get; set; } = "";
            public string Location { get; set; } = "";
            public string FacilityType { get; set; } = "";
            public string OwnerName { get; set; } = "";
            public string ContactNumber { get; set; } = "";
            public decimal ActualCapacity { get; set; }
            public decimal TotalArea { get; set; }
            public int NumberOfChambers { get; set; }
            public string YearEstablished { get; set; } = "";
            public string QCStatus { get; set; } = "";
            public DateTime QCDate { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }
    }
}