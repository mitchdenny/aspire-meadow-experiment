var builder = DistributedApplication.CreateBuilder(args);

builder.AddMeadowProject<Projects.AspireMeadowExperiment_TiltSensor>("tiltsensor");

builder.Build().Run();
