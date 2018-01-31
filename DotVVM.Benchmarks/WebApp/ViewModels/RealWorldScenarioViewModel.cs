using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using DotVVM.Framework.ViewModel;

namespace DotVVM.Benchmarks.WebApp.ViewModels
{
    public class RealWorldScenarioViewModel : DotvvmViewModelBase
    {
        [Bind(Direction.ServerToClient)]
        public string ErrorMessage { get; set; }

        public string Title { get; set; } = "Sample Title";

        [Bind(Direction.ServerToClient)]
        public string SuccessMessage { get; set; }

        [Bind(Direction.ServerToClientFirstRequest)]
        public SessionDTO Session => new SessionDTO
        {
            Name = "David Lister",
            OrganizationName = "Red Dwarf",
            HasMultipleOrganizations = true,
        };

        public bool SignedIn { get; set; }
        public bool IsOrganizationModalDisplayed { get; set; }
        public string CurrentRoute => Context.Route.RouteName;

        public bool ContainsCategory { get; set; }
        public bool ContainsAutomaticDecision { get; set; }
        public bool ContainsReceivers { get; set; }
        public bool ContainsLocationsAndPersons { get; set; }

        public List<string> ResponsiblePersons { get; set; } = new List<string>();
        public IEnumerable<PersonalDataExtDTO> PersonalData { get; set; }
        public PersonalDataExtDTO SelectedPersonalData { get; set; }

        public List<PersonalDataDTO> PersonalDataList { get; set; }

        public override Task Load()
        {
            if (!ResponsiblePersons.Any())
            {
                ResponsiblePersons.Add("Ashe");
                ResponsiblePersons.Add("Lulu");
                ResponsiblePersons.Add("Braum");
                ResponsiblePersons.Add("Draven");
            }

            return base.Load();
        }

        public override Task PreRender()
        {
            ContainsCategory = true;
            ContainsAutomaticDecision = false;
            ContainsReceivers = true;
            ContainsLocationsAndPersons = true;

            if (!Context.IsPostBack)
            {
                PersonalData = new List<PersonalDataExtDTO> {
                    new PersonalDataExtDTO { Category = true, Id = 1, Name = "Name1"},
                    new PersonalDataExtDTO { Category = true, Id = 2, Name = "Name2", AutomaticDecision = true},
                    new PersonalDataExtDTO { Category = true, Id = 3, Name = "Name3", OrganizationId = 3},
                };

                PersonalDataList = new List<PersonalDataDTO> {
                    new PersonalDataDTO { LegalReason = "Reason1", Id = 1, Name = "Name1", CreatedAt = DateTime.Now},
                    new PersonalDataDTO { LegalReason = "Reason2", Id = 2, Name = "Name2", CreatedAt = DateTime.UtcNow},
                    new PersonalDataDTO { LegalReason = "Reason3", Id = 3, Name = "Name3", CreatedAt = DateTime.Now.AddDays(1), OrganizationId = 3},
                };
            }

            return base.PreRender();
        }

        public void SignOut()
        {
            SignedIn = false;
        }

        public void ShowOrganizations()
        {
            IsOrganizationModalDisplayed = true;
        }

        public void ShowReceiversModal(int id)
        {
            SelectedPersonalData = PersonalData.FirstOrDefault(p => p.Id == id);
        }

        public void Remove(PersonalDataDTO personalData)
        {
            PersonalDataList.Remove(personalData);
        }

        [AllowStaticCommand]
        public static List<PersonalDataDTO> InsertNewRow(int purposeId, List<PersonalDataDTO> data)
        {
            var selectedPurpose = data.FirstOrDefault(p => p.Id == purposeId);

            if (selectedPurpose != null)
            {
                var index = data.IndexOf(selectedPurpose);
            }

            return data;
        }

        public class SessionDTO
        {
            public string Name { get; set; }
            public string OrganizationName { get; set; }
            public bool HasMultipleOrganizations { get; set; }
        }

        public class PersonalDataExtDTO
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int OrganizationId { get; set; }

            public bool Category { get; set; }
            public bool AutomaticDecision { get; set; }

            public List<ReceiverDTO> Receivers { get; set; } = new List<ReceiverDTO>();
            public string CompetentPersons { get; set; }
        }

        public class ReceiverDTO
        {
            public int Id { get; set; }
            public string RegistrationId { get; set; }
            public string Name { get; set; }
        }

        public class PersonalDataDTO
        {
            public int Id { get; set; }
            [Required(AllowEmptyStrings = false, ErrorMessage = "The field is required")]
            public string Name { get; set; }
            [Required(ErrorMessage = "The field is required")]
            public int? PurposeId { get; set; }
            public int OrganizationId { get; set; }
            [Bind(Direction.None)]
            public DateTime? ProcessedAt { get; set; }
            public string LegalReason { get; set; }

            public bool SentToProcess { get; set; }
            public string Result => LegalReason ?? (SentToProcess ? "To be processed" : "Will be processed");

            public string Note { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? LastModifiedAt { get; set; }
            public string ShreddingPeriod { get; set; }

            public int Order { get; set; }

            public IEnumerable<Purpose> Purposes => new Purpose[] {
                new Purpose { Name = "Name1", Id = 1 },
                new Purpose { Name = "Name2", Id = 2 },
                new Purpose { Name = "Name3", Id = 3, Note = "Note" },
            };
        }

        public class Purpose
        {
            public string Name { get; set; }
            public string Note { get; set; }
            public int Id { get; set; }
        }
    }

    public static class TitleResources
    {
        public static string Gdpr = "Gdpr title";
        public static string Users = "Users title";
        public static string Activity = "Activity title";
        public static string Help = "Help title";
    }
}
