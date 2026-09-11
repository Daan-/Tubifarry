using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Spotify;

namespace Tubifarry.ImportLists.Spotify
{
    public class SpotifySavedTracksSettingsValidator : SpotifySettingsBaseValidator<SpotifySavedTracksSettings>
    {
        public SpotifySavedTracksSettingsValidator() : base()
        {
        }
    }

    public class SpotifySavedTracksSettings : SpotifySettingsBase<SpotifySavedTracksSettings>
    {
        protected override AbstractValidator<SpotifySavedTracksSettings> Validator => new SpotifySavedTracksSettingsValidator();

        public override string Scope => "user-library-read";

        [FieldDefinition(1, Label = "Include Singles", Type = FieldType.Checkbox, HelpText = "Import liked songs whose album is a single or an appears-on compilation. Leaving this off keeps only tracks that belong to a real album.", Advanced = true)]
        public bool IncludeSingles { get; set; }
    }
}
