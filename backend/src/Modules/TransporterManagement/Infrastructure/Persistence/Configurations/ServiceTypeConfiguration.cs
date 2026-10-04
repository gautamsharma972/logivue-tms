using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceTypeDefinition>
{
    public void Configure(EntityTypeBuilder<ServiceTypeDefinition> b)
    {
        b.ToTable("tm_service_types");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(
            new ServiceTypeDefinition { Id = 1, Code = "FTL", Name = "Full Truck Load" },
            new ServiceTypeDefinition { Id = 2, Code = "PTL", Name = "Part Truck Load" },
            new ServiceTypeDefinition { Id = 3, Code = "EXPRESS", Name = "Express" },
            new ServiceTypeDefinition { Id = 4, Code = "DEDICATED", Name = "Dedicated" },
            new ServiceTypeDefinition { Id = 5, Code = "LAST_MILE", Name = "Last Mile" },
            new ServiceTypeDefinition { Id = 6, Code = "MILK_RUN", Name = "Milk Run" });
    }
}
