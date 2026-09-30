using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteUniqueIndexesAreLiveRowsOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_schools_UserId_SchoolId_IsDeleted",
                table: "user_schools");

            migrationBuilder.DropIndex(
                name: "IX_terms_SchoolId_SchoolYearId_Order_IsDeleted",
                table: "terms");

            migrationBuilder.DropIndex(
                name: "IX_school_years_SchoolId_Label_IsDeleted",
                table: "school_years");

            migrationBuilder.DropIndex(
                name: "IX_rooms_SchoolId_BuildingId_Name_IsDeleted",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_mentions_SchoolId_Label_IsDeleted",
                table: "mentions");

            migrationBuilder.DropIndex(
                name: "IX_inventory_categories_SchoolId_Name_IsDeleted",
                table: "inventory_categories");

            migrationBuilder.DropIndex(
                name: "IX_fee_categories_SchoolId_Name_IsDeleted",
                table: "fee_categories");

            migrationBuilder.DropIndex(
                name: "IX_classrooms_SchoolId_Name_IsDeleted",
                table: "classrooms");

            migrationBuilder.DropIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId_IsDeleted",
                table: "class_fees");

            migrationBuilder.DropIndex(
                name: "IX_buildings_SchoolId_Name_IsDeleted",
                table: "buildings");

            migrationBuilder.CreateIndex(
                name: "IX_user_schools_UserId_SchoolId",
                table: "user_schools",
                columns: new[] { "UserId", "SchoolId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_terms_SchoolId_SchoolYearId_Order",
                table: "terms",
                columns: new[] { "SchoolId", "SchoolYearId", "Order" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_school_years_SchoolId_Label",
                table: "school_years",
                columns: new[] { "SchoolId", "Label" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_rooms_SchoolId_BuildingId_Name",
                table: "rooms",
                columns: new[] { "SchoolId", "BuildingId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_mentions_SchoolId_Label",
                table: "mentions",
                columns: new[] { "SchoolId", "Label" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_categories_SchoolId_Name",
                table: "inventory_categories",
                columns: new[] { "SchoolId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_fee_categories_SchoolId_Name",
                table: "fee_categories",
                columns: new[] { "SchoolId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_classrooms_SchoolId_Name",
                table: "classrooms",
                columns: new[] { "SchoolId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId",
                table: "class_fees",
                columns: new[] { "SchoolId", "FeeCategoryId", "ClassroomId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_buildings_SchoolId_Name",
                table: "buildings",
                columns: new[] { "SchoolId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_schools_UserId_SchoolId",
                table: "user_schools");

            migrationBuilder.DropIndex(
                name: "IX_terms_SchoolId_SchoolYearId_Order",
                table: "terms");

            migrationBuilder.DropIndex(
                name: "IX_school_years_SchoolId_Label",
                table: "school_years");

            migrationBuilder.DropIndex(
                name: "IX_rooms_SchoolId_BuildingId_Name",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_mentions_SchoolId_Label",
                table: "mentions");

            migrationBuilder.DropIndex(
                name: "IX_inventory_categories_SchoolId_Name",
                table: "inventory_categories");

            migrationBuilder.DropIndex(
                name: "IX_fee_categories_SchoolId_Name",
                table: "fee_categories");

            migrationBuilder.DropIndex(
                name: "IX_classrooms_SchoolId_Name",
                table: "classrooms");

            migrationBuilder.DropIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId",
                table: "class_fees");

            migrationBuilder.DropIndex(
                name: "IX_buildings_SchoolId_Name",
                table: "buildings");

            migrationBuilder.CreateIndex(
                name: "IX_user_schools_UserId_SchoolId_IsDeleted",
                table: "user_schools",
                columns: new[] { "UserId", "SchoolId", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_terms_SchoolId_SchoolYearId_Order_IsDeleted",
                table: "terms",
                columns: new[] { "SchoolId", "SchoolYearId", "Order", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_school_years_SchoolId_Label_IsDeleted",
                table: "school_years",
                columns: new[] { "SchoolId", "Label", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_SchoolId_BuildingId_Name_IsDeleted",
                table: "rooms",
                columns: new[] { "SchoolId", "BuildingId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mentions_SchoolId_Label_IsDeleted",
                table: "mentions",
                columns: new[] { "SchoolId", "Label", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_categories_SchoolId_Name_IsDeleted",
                table: "inventory_categories",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_categories_SchoolId_Name_IsDeleted",
                table: "fee_categories",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classrooms_SchoolId_Name_IsDeleted",
                table: "classrooms",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId_IsDeleted",
                table: "class_fees",
                columns: new[] { "SchoolId", "FeeCategoryId", "ClassroomId", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_buildings_SchoolId_Name_IsDeleted",
                table: "buildings",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);
        }
    }
}
