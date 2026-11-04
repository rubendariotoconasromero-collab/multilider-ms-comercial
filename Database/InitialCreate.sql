IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE TABLE [Asesores] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NULL,
        [NombreCompleto] nvarchar(1000) NOT NULL,
        [Telefono] nvarchar(1000) NULL,
        [Correo] nvarchar(1000) NULL,
        [Direccion] nvarchar(1000) NULL,
        [Estado] bit NOT NULL,
        [PorcentajeComision] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Asesores] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE TABLE [Clientes] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NULL,
        [Ci] nvarchar(50) NOT NULL,
        [LugarExpedicion] nvarchar(1000) NULL,
        [NombreCompleto] nvarchar(1000) NOT NULL,
        [Telefono] nvarchar(1000) NULL,
        [Correo] nvarchar(1000) NULL,
        [Direccion] nvarchar(1000) NULL,
        [Estado] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Clientes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE TABLE [Ventas] (
        [Id] int NOT NULL IDENTITY,
        [SolicitudId] uniqueidentifier NOT NULL,
        [SolicitudHash] nvarchar(64) NOT NULL,
        [ClienteId] int NOT NULL,
        [AsesorId] int NOT NULL,
        [PropiedadId] int NOT NULL,
        [PropiedadActivaId] int NULL,
        [CodigoPropiedad] nvarchar(1000) NOT NULL,
        [Moneda] nvarchar(1000) NOT NULL,
        [Fecha] date NOT NULL,
        [TipoVenta] nvarchar(1000) NOT NULL,
        [MontoTotal] decimal(18,2) NOT NULL,
        [Descuento] decimal(18,2) NOT NULL,
        [MontoLiquido] decimal(18,2) NOT NULL,
        [MontoComision] decimal(18,2) NOT NULL,
        [CuotaInicial] decimal(18,2) NOT NULL,
        [SaldoCredito] decimal(18,2) NOT NULL,
        [NumeroCuotas] int NOT NULL,
        [TasaInteres] decimal(18,2) NOT NULL,
        [TasaMora] decimal(18,2) NOT NULL,
        [FechaInicioPago] date NULL,
        [Estado] nvarchar(1000) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Ventas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Ventas_Asesores_AsesorId] FOREIGN KEY ([AsesorId]) REFERENCES [Asesores] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Ventas_Clientes_ClienteId] FOREIGN KEY ([ClienteId]) REFERENCES [Clientes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE TABLE [Contratos] (
        [Id] int NOT NULL IDENTITY,
        [NotaVentaId] int NOT NULL,
        [CodigoContrato] nvarchar(100) NOT NULL,
        [FechaEmision] date NOT NULL,
        [FechaFirma] date NULL,
        [Estado] nvarchar(1000) NOT NULL,
        [DocumentoId] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Contratos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Contratos_Ventas_NotaVentaId] FOREIGN KEY ([NotaVentaId]) REFERENCES [Ventas] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE TABLE [Entregas] (
        [Id] int NOT NULL IDENTITY,
        [ContratoId] int NOT NULL,
        [FechaProgramada] date NOT NULL,
        [FechaEntrega] date NULL,
        [Estado] nvarchar(1000) NOT NULL,
        [CondicionInmueble] nvarchar(1000) NULL,
        [ItemsEntregados] nvarchar(1000) NULL,
        [Observaciones] nvarchar(1000) NULL,
        [EntregadoPor] nvarchar(1000) NULL,
        [RecibidoPor] nvarchar(1000) NULL,
        [ActaId] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Entregas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Entregas_Contratos_ContratoId] FOREIGN KEY ([ContratoId]) REFERENCES [Contratos] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Clientes_Ci] ON [Clientes] ([Ci]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Contratos_CodigoContrato] ON [Contratos] ([CodigoContrato]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Contratos_NotaVentaId] ON [Contratos] ([NotaVentaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Entregas_ContratoId] ON [Entregas] ([ContratoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Ventas_AsesorId] ON [Ventas] ([AsesorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Ventas_ClienteId] ON [Ventas] ([ClienteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Ventas_PropiedadActivaId] ON [Ventas] ([PropiedadActivaId]) WHERE [PropiedadActivaId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Ventas_SolicitudId] ON [Ventas] ([SolicitudId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104160242_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104160242_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

