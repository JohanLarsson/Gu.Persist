﻿namespace Gu.Persist.NewtonsoftJson.Tests.Repository
{
    using Gu.Persist.Core;

    using RepositorySettings = Gu.Persist.NewtonsoftJson.RepositorySettings;

    public class SaveNullDeletesFileCaching : JsonRepositoryTests
    {
        protected override IRepository Create()
        {
            var settings = new NewtonsoftJson.DataRepositorySettings(
                PathAndSpecialFolder.Create(this.Directory),
                RepositorySettings.CreateDefaultJsonSettings(),
                true,
                true,
                BackupSettings.Create(this.Directory));
            return new DataRepository(settings);
        }
    }
}