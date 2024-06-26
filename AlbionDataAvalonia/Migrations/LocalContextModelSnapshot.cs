﻿// <auto-generated />
using System;
using AlbionDataAvalonia.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace AlbionDataAvalonia.Migrations
{
    [DbContext(typeof(LocalContext))]
    partial class LocalContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.5");

            modelBuilder.Entity("AlbionDataAvalonia.Network.Models.AlbionMail", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("AlbionServerId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("AuctionType")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Deleted")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsSet")
                        .HasColumnType("INTEGER");

                    b.Property<string>("ItemId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("LocationId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("PartialAmount")
                        .HasColumnType("INTEGER");

                    b.Property<string>("PlayerName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("Received")
                        .HasColumnType("TEXT");

                    b.Property<double>("TaxesPercent")
                        .HasColumnType("REAL");

                    b.Property<int>("TotalAmount")
                        .HasColumnType("INTEGER");

                    b.Property<long>("TotalSilver")
                        .HasColumnType("INTEGER");

                    b.Property<long>("TotalTaxes")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Type")
                        .HasColumnType("INTEGER");

                    b.Property<long>("UnitSilver")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("Id")
                        .IsUnique();

                    b.HasIndex("TotalSilver");

                    b.HasIndex("AlbionServerId", "LocationId", "AuctionType", "Deleted", "Received");

                    b.ToTable("AlbionMails");
                });
#pragma warning restore 612, 618
        }
    }
}
