
using FaceAttendanceSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace FaceAttendanceSystem.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Database Tables
    public DbSet<Employee> Employees { get; set; }
    public DbSet<Attendance> Attendances { get; set; }
    public DbSet<Schedule> Schedules { get; set; }
    public DbSet<ScheduleEmployeeAssignment> ScheduleEmployeeAssignments { get; set; }

    public DbSet<Admin> Admins { get; set; }
    public DbSet<AdminSession> AdminSessions { get; set; }
    public DbSet<Department> Departments { get; set; }

    public DbSet<Location> Locations { get; set; }
    public DbSet<EmployeeDeletionAudit> EmployeeDeletionAudits { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================
        // MySQL Table Name Mappings
        // ==========================================
        // Linux MySQL is case-sensitive.
        // Match the existing lowercase table names
        // imported from the original database.

        modelBuilder.Entity<Employee>()
            .ToTable("employees");

        modelBuilder.Entity<Attendance>()
            .ToTable("attendances");

        modelBuilder.Entity<Schedule>()
            .ToTable("schedules");

        modelBuilder.Entity<ScheduleEmployeeAssignment>()
            .ToTable("scheduleemployeeassignments");

        modelBuilder.Entity<Admin>()
            .ToTable("admins");

        modelBuilder.Entity<AdminSession>()
            .ToTable("adminsessions");

        modelBuilder.Entity<Department>()
            .ToTable("departments");

        modelBuilder.Entity<Location>()
            .ToTable("locations");

        modelBuilder.Entity<EmployeeDeletionAudit>()
            .ToTable("employeedeletionaudits");

        // ==========================================
        // Admin Session Relationships
        // ==========================================

        modelBuilder.Entity<AdminSession>()
            .HasOne(s => s.Admin)
            .WithMany()
            .HasForeignKey(s => s.AdminId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AdminSession>()
            .HasIndex(s => s.SessionId)
            .IsUnique();

        // ==========================================
        // Schedule Employee Relationships
        // ==========================================

        modelBuilder.Entity<ScheduleEmployeeAssignment>()
            .HasOne(a => a.Schedule)
            .WithMany(s => s.EmployeeAssignments)
            .HasForeignKey(a => a.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleEmployeeAssignment>()
            .HasOne(a => a.Employee)
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ScheduleEmployeeAssignment>()
            .HasIndex(a => new
            {
                a.ScheduleId,
                a.EmployeeId
            })
            .IsUnique();

        // ==========================================
        // Employee Unique Index
        // ==========================================

        modelBuilder.Entity<Employee>()
            .HasIndex(e => e.EmployeeId)
            .IsUnique();
    }
}

