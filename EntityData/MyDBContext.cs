﻿// -----------------------------------------------------------------------
// <copyright file="MyDBContext.cs" company="">
// Copyright 2013 Alexander Soffronow Pagonidis
// </copyright>
// -----------------------------------------------------------------------

using System.Data.Entity;
using QDMS;

namespace EntityData
{
    public partial class MyDBContext : DbContext
    {
        public MyDBContext()
            : base("Name=qdmsEntitiesMySql")
        {
        }
 
        public DbSet<Instrument> Instruments { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<Exchange> Exchanges { get; set; }
        public DbSet<Datasource> Datasources { get; set; }
        public DbSet<SessionTemplate> SessionTemplates { get; set; }
        public DbSet<ExchangeSession> ExchangeSessions { get; set; }
        public DbSet<InstrumentSession> InstrumentSessions { get; set; }
        public DbSet<TemplateSession> TemplateSessions { get; set; }
        public DbSet<UnderlyingSymbol> UnderlyingSymbols { get; set; }
        public DbSet<ContinuousFuture> ContinuousFutures { get; set; }
 
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExchangeSession>().ToTable("exchangesessions");
            modelBuilder.Entity<InstrumentSession>().ToTable("instrumentsessions");
            modelBuilder.Entity<TemplateSession>().ToTable("templatesessions");
           

            modelBuilder.Configurations.Add(new InstrumentConfig());
            modelBuilder.Configurations.Add(new TagConfig());
            modelBuilder.Configurations.Add(new ExchangeConfig());
            modelBuilder.Configurations.Add(new DatasourceConfig());
            modelBuilder.Configurations.Add(new UnderlyingSymbolConfig());
            modelBuilder.Configurations.Add(new ContinuousFutureConfig());

            modelBuilder.Entity<Instrument>()
            .HasMany(c => c.Tags)
            .WithMany()             
            .Map(x =>
            {
                x.MapLeftKey("InstrumentID");
                x.MapRightKey("TagID");
                x.ToTable("tag_map");
            });
                
        }
    }
}
