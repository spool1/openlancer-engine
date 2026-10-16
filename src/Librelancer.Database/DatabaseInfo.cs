using System;

namespace LibreLancer.Database;

public class DatabaseInfo
{
    public int Id { get; set; } = 1;
    public Guid DatabaseGuid { get; set; } = Guid.NewGuid();
}
