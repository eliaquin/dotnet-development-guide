CREATE TABLE Users (
    Id TEXT PRIMARY KEY NOT NULL CHECK (length(Id) > 0),
    UserName TEXT NOT NULL CHECK (length(UserName) BETWEEN 1 AND 50),
    NormalizedUserName TEXT NOT NULL UNIQUE CHECK (length(NormalizedUserName) BETWEEN 1 AND 50),
    PasswordHash TEXT NOT NULL CHECK (length(PasswordHash) > 0),
    SecurityStamp TEXT NOT NULL CHECK (length(SecurityStamp) > 0),
    ConcurrencyStamp TEXT NOT NULL CHECK (length(ConcurrencyStamp) > 0),
    AccessFailedCount INTEGER NOT NULL DEFAULT 0 CHECK (AccessFailedCount BETWEEN 0 AND 2147483647),
    LockoutEnabled INTEGER NOT NULL DEFAULT 1 CHECK (LockoutEnabled IN (0, 1)),
    LockoutEnd TEXT NULL
) STRICT;

CREATE TABLE UserRoles (
    UserId TEXT NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    RoleName TEXT NOT NULL CHECK (RoleName IN ('Reader', 'Editor', 'Admin')),
    PRIMARY KEY (UserId, RoleName)
) STRICT;

CREATE INDEX IX_UserRoles_RoleName_UserId ON UserRoles(RoleName, UserId);
