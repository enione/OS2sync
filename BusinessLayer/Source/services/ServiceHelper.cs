﻿using System;
using Organisation.IntegrationLayer;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Organisation.BusinessLayer.DTO.Registration;
using IntegrationLayer.OrganisationFunktion;

namespace Organisation.BusinessLayer
{
    class ServiceHelper
    {
        private static log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static OrganisationFunktionStub organisationFunktionStub = new OrganisationFunktionStub();
        private static AdresseStub adresseStub = new AdresseStub();
        private static PersonStub personStub = new PersonStub();

        internal static List<FiltreretOejebliksbilledeType> FindUnitRolesForUser(string uuid, List<FiltreretOejebliksbilledeType> allUnitRoles = null)
        {
            if (allUnitRoles != null)
            {
                var unitRoles = new List<FiltreretOejebliksbilledeType>();
                foreach (var unitRole in allUnitRoles)
                {
                    if (unitRole.Registrering.Length == 0)
                    {
                        continue;
                    }

                    if (unitRole.Registrering[0].RelationListe?.TilknyttedeBrugere == null)
                    {
                        continue;
                    }

                    if (unitRole.Registrering[0].RelationListe.TilknyttedeBrugere.Length == 0)
                    {
                        continue;
                    }

                    foreach (var brugerRelation in unitRole.Registrering[0].RelationListe.TilknyttedeBrugere)
                    {
                        if (brugerRelation.ReferenceID.Item.Equals(uuid))
                        {
                            unitRoles.Add(unitRole);
                        }
                    }
                }

                return unitRoles;
            }
            else
            {
                return organisationFunktionStub.SoegAndGetLatestRegistration(UUIDConstants.ORGFUN_POSITION, uuid, null, null);
            }
        }

        internal static List<FiltreretOejebliksbilledeType> FindUnitRolesForOrgUnitAsObjects(string uuid)
        {
            return organisationFunktionStub.SoegAndGetLatestRegistration(UUIDConstants.ORGFUN_POSITION, null, uuid, null);
        }

        internal static List<FiltreretOejebliksbilledeType> FindManagerRolesForOrgUnitAsObjects(string uuid)
        {
            return organisationFunktionStub.SoegAndGetLatestRegistration(UUIDConstants.ORGFUN_MANAGER, null, uuid, null);
        }

        internal static void ImportAddress(string address, DateTime timestamp, out string uuid)
        {
            uuid = null;

            if (address == null)
            {
                return;
            }

            AddressData addressData = new AddressData()
            {
                AddressText = address,
                Timestamp = timestamp
            };

            adresseStub.Importer(addressData);

            uuid = addressData.Uuid;
        }

        internal static void UpdateManager(OrgUnitRegistration orgUnit)
        {
            /*
               1. fetch any existing manager relationship (expect 0 or 1, anything else is a bug)
               2. compare with existing to see if changes are needed (or simply create if none exist)
                  a) a "manager" is a match if it points to the same manager (user uuid)
             */

            // fetch all existing managerRoles
            List<FiltreretOejebliksbilledeType> managerRoles = FindManagerRolesForOrgUnitAsObjects(orgUnit.Uuid);
            if (managerRoles != null && managerRoles.Count > 1)
            {
                log.Warn("OrgUnit had more than one manager - deleting all, and creating new manager: " + orgUnit.Uuid);

                foreach (var role in managerRoles)
                {
                    if (role.ObjektType?.UUIDIdentifikator != null)
                    {
                        organisationFunktionStub.Deactivate(role.ObjektType.UUIDIdentifikator, orgUnit.Timestamp);
                    }
                }

                // null, and deal as if none existed
                managerRoles = null;
            }

            string existingManagerUuid = null;
            if (managerRoles != null && managerRoles.Count == 1)
            {
                var role = managerRoles[0];

                if (role.Registrering != null && role.Registrering.Length > 0 && role.Registrering[0].RelationListe?.TilknyttedeBrugere != null && role.Registrering[0].RelationListe.TilknyttedeBrugere.Length > 0)
                {
                    existingManagerUuid = role.Registrering[0].RelationListe.TilknyttedeBrugere[0].ReferenceID?.Item;
                }

                // check for bad registration, and delete registration if is is broken
                if (string.IsNullOrEmpty(existingManagerUuid))
                {
                    organisationFunktionStub.Deactivate(role.ObjektType.UUIDIdentifikator, orgUnit.Timestamp);
                }
            }

            if (!string.IsNullOrEmpty(existingManagerUuid) && !existingManagerUuid.ToLower().Equals(orgUnit.ManagerUuid))
            {
                if (string.IsNullOrEmpty(orgUnit.ManagerUuid))
                {
                    // delete
                    organisationFunktionStub.Deactivate(managerRoles[0].ObjektType.UUIDIdentifikator, orgUnit.Timestamp);
                }
                else
                {
                    // update
                    organisationFunktionStub.Ret(new OrgFunctionData()
                    {
                        Uuid = managerRoles[0].ObjektType.UUIDIdentifikator,
                        ShortKey = managerRoles[0].Registrering[0].AttributListe.Egenskab[0].BrugervendtNoegleTekst,
                        Name = managerRoles[0].Registrering[0].AttributListe.Egenskab[0].FunktionNavn,
                        FunctionTypeUuid = UUIDConstants.ORGFUN_MANAGER,
                        OrgUnits = new List<string>() { orgUnit.Uuid },
                        Users = new List<string>() { orgUnit.ManagerUuid },
                        Timestamp = orgUnit.Timestamp
                    }, UpdateIndicator.COMPARE, UpdateIndicator.NONE, UpdateIndicator.NONE);
                }
            }
            else if (!string.IsNullOrEmpty(orgUnit.ManagerUuid) && !orgUnit.ManagerUuid.Equals(existingManagerUuid))
            {
                // create new manager role
                organisationFunktionStub.Importer(new OrgFunctionData()
                {
                    Uuid = IdUtil.GenerateUuid(),
                    ShortKey = IdUtil.GenerateShortKey(),
                    Name = "Leder",
                    FunctionTypeUuid = UUIDConstants.ORGFUN_MANAGER,
                    OrgUnits = new List<string>() { orgUnit.Uuid },
                    Users = new List<string>() { orgUnit.ManagerUuid },
                    Timestamp = orgUnit.Timestamp
                });
            }
        }

        internal static void UpdatePosition(UserRegistration user)
        {
            /*
               1. fetch all positions that the user currently have
               2. compare positions from organisation with the positions in the registration, using the following rules
                  a) a position is a "match" if it points to the same unit (yes, this is an issue for the users with multiple positions in the same OU, luckily those are few)
                  b) a position should be updated if the name/ou-pointer has been changed
                  c) a position should be removed if it no longer exist in the registration, but does in organisation
                  d) a position should be added, if it exists in the registration, but not in organisation
             */

            // fetch all the users existing positions
            List<FiltreretOejebliksbilledeType> unitRoles = FindUnitRolesForUser(user.Uuid);

            List<Position> copyOfUserPositions = new List<Position>(user.Positions);

            // loop through roles found in organisation, and find those that must be updated, and those that must be deleted
            foreach (FiltreretOejebliksbilledeType unitRole in unitRoles)
            {
                RegistreringType1 existingRoleRegistration = unitRole.Registrering[0];

                if (existingRoleRegistration.RelationListe.TilknyttedeEnheder.Length != 1)
                {
                    log.Warn("User '" + user.Uuid + "' has an existing position in Organisation with " + existingRoleRegistration.RelationListe.TilknyttedeEnheder.Length + " associated OrgUnits");
                    continue;
                }

                // figure out everything relevant about the position object in Organisation
                EgenskabType latestProperty = StubUtil.GetLatestProperty(existingRoleRegistration.AttributListe.Egenskab);
                string existingRoleUuid = unitRole.ObjektType.UUIDIdentifikator;
                string existingRoleOUUuid = existingRoleRegistration.RelationListe.TilknyttedeEnheder[0].ReferenceID.Item;
                string existingRoleName = latestProperty.FunktionNavn;
                string existingRoleShortKey = latestProperty.BrugervendtNoegleTekst;

                bool found = false;
                for (int i = copyOfUserPositions.Count - 1; i >= 0; i--)
                {
                    Position position = copyOfUserPositions[i];

                    if (existingRoleOUUuid.Equals(position.OrgUnitUuid))
                    {
                        // update if needed
                        if (string.Compare(existingRoleName, position.Name) != 0)
                        {
                            organisationFunktionStub.Ret(new OrgFunctionData()
                            {
                                Uuid = existingRoleUuid,
                                ShortKey = existingRoleShortKey,
                                Name = position.Name,
                                FunctionTypeUuid = UUIDConstants.ORGFUN_POSITION,
                                OrgUnits = new List<string>() { position.OrgUnitUuid },
                                Users = new List<string>() { user.Uuid },
                                Timestamp = user.Timestamp
                            }, UpdateIndicator.NONE, UpdateIndicator.COMPARE, UpdateIndicator.NONE);

                            // copy value to read data, so we can use it for comparison below when deciding what to create
                            latestProperty.FunktionNavn = position.Name;
                        }

                        // ensure that the updated positions i no longer in the copied list, so multiple positions
                        // in the same OU, will not hit the same source-position
                        copyOfUserPositions.RemoveAt(i);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    organisationFunktionStub.Deactivate(existingRoleUuid, user.Timestamp);
                }
            }

            // refresh copy of positions
            var copyOfRemotePositions = new List<FiltreretOejebliksbilledeType>(unitRoles);

            // loop through all roles found in the local registration, and create all those that do not exist in Organisation
            foreach (var position in user.Positions)
            {
                bool found = false;

                for (int i = copyOfRemotePositions.Count - 1; i >= 0; i--)
                {
                    var unitRole = copyOfRemotePositions[i];
                    RegistreringType1 existingRoleRegistration = unitRole.Registrering[0];

                    if (existingRoleRegistration.RelationListe.TilknyttedeEnheder.Length != 1)
                    {
                        log.Warn("User '" + user.Uuid + "' has an existing position in Organisation with " + existingRoleRegistration.RelationListe.TilknyttedeEnheder.Length + " associated OrgUnits");
                        continue;
                    }

                    string existingRoleOUUuid = existingRoleRegistration.RelationListe.TilknyttedeEnheder[0].ReferenceID.Item;
                    string existingFunctionName = existingRoleRegistration.AttributListe.Egenskab[0].FunktionNavn;

                    if (existingRoleOUUuid.Equals(position.OrgUnitUuid) && string.Compare(existingFunctionName, position.Name) == 0)
                    {
                        // to make sure we can add new positions within the same OU as existing positions, we
                        // remove existing ones, one at a time, so we do not match twice
                        copyOfRemotePositions.RemoveAt(i);
                        found = true;
                    }
                }

                if (!found)
                {
                    organisationFunktionStub.Importer(new OrgFunctionData()
                    {
                        Uuid = IdUtil.GenerateUuid(),
                        ShortKey = IdUtil.GenerateShortKey(),
                        Name = position.Name,
                        FunctionTypeUuid = UUIDConstants.ORGFUN_POSITION,
                        OrgUnits = new List<string>() { position.OrgUnitUuid },
                        Users = new List<string>() { user.Uuid },
                        Timestamp = user.Timestamp
                    });
                }
            }
        }

        // this method must be synchronized, as we create a function on the first entry into this method, and return the same value on all other calls
        [MethodImpl(MethodImplOptions.Synchronized)]
        internal static string EnsurePayoutUnitFunctionExists(string payoutUnitUuid, DateTime timestamp)
        {
            // if there is an existing function, just return the uuid
            List<string> existingFunctions = organisationFunktionStub.SoegAndGetUuids(UUIDConstants.ORGFUN_PAYOUT_UNIT, null, payoutUnitUuid, null);
            if (existingFunctions != null && existingFunctions.Count > 0)
            {
                return existingFunctions[0];
            }

            // otherwise create a new function
            OrgFunctionData orgFunction = new OrgFunctionData();
            orgFunction.FunctionTypeUuid = UUIDConstants.ORGFUN_PAYOUT_UNIT;
            orgFunction.Name = "PayoutUnitFunction";
            orgFunction.OrgUnits.Add(payoutUnitUuid);
            orgFunction.ShortKey = IdUtil.GenerateShortKey();
            orgFunction.Timestamp = timestamp;
            orgFunction.Uuid = IdUtil.GenerateUuid();

            organisationFunktionStub.Importer(orgFunction);

            return orgFunction.Uuid;
        }

        internal static void UpdatePerson(UserRegistration user, string orgPersonUuid, out string uuid)
        {
            uuid = orgPersonUuid; // default

            if (orgPersonUuid != null)
            {
                // This is the expected case for an update operation - see if we have updates for the referenced Person object

                personStub.Ret(orgPersonUuid, user.Person.Name, user.Person.Cpr, user.Timestamp);
            }
            else
            {
                // This is either because no Person object existed for the User (first time creation), or because
                // the local supplied uuid of the Person object differs from the one stored in Organisation. In both
                // cases we need to create the Person object from scratch

                PersonData personData = new PersonData()
                {
                    Cpr = user.Person.Cpr,
                    Name = user.Person.Name,
                    ShortKey = IdUtil.GenerateShortKey(),
                    Timestamp = user.Timestamp,
                    Uuid = IdUtil.GenerateUuid()
                };

                personStub.Importer(personData);

                // ensure that we have the uuid of the person in the user object, as we will need it for later
                uuid = personData.Uuid;
            }
        }

        internal static void UpdateAddress(string address, string orgUuid, DateTime timestamp, out string uuid)
        {
            uuid = orgUuid; // default to input - the caller can then ALWAYS use this for relationship mapping

            if (address != null)
            {
                if (orgUuid == null)
                {
                    ImportAddress(address, timestamp, out uuid);
                }
                else
                {
                    var result = adresseStub.GetLatestRegistration(orgUuid);
                    if (result == null)
                    {
                        ImportAddress(address, timestamp, out uuid);
                    }
                    else
                    {
                        adresseStub.Ret(orgUuid, address, timestamp, result);
                    }
                }
            }
        }

        internal static List<string> GetContactForTasks(string uuid)
        {
            var result = new List<string>();

            var existingFunctions = organisationFunktionStub.SoegAndGetLatestRegistration(UUIDConstants.ORGFUN_CONTACT_UNIT, null, uuid, null);
            if (existingFunctions != null && existingFunctions.Count > 0)
            {
                var existingFunction = existingFunctions[0];

                if (existingFunction.Registrering.Length > 0 && existingFunction.Registrering[0].RelationListe?.Opgaver != null)
                {
                    var opgaver = existingFunction.Registrering[0].RelationListe.Opgaver;

                    foreach (var opgave in opgaver)
                    {
                        if (opgave.ReferenceID?.Item != null)
                        {
                            result.Add(opgave.ReferenceID.Item);
                        }
                    }
                }
            }

            return result;
        }

        internal static void DeleteContactForTasks(string uuid, DateTime timestamp)
        {
            // search for existing OrgFunction
            var existingFunctions = organisationFunktionStub.SoegAndGetLatestRegistration(UUIDConstants.ORGFUN_CONTACT_UNIT, null, uuid, null);

            // if no function exists, do nothing
            if (existingFunctions == null || existingFunctions.Count == 0)
            {
                return;
            }

            organisationFunktionStub.Deactivate(existingFunctions[0].ObjektType.UUIDIdentifikator, timestamp);
        }

        internal static void UpdateContactForTasks(string uuid, List<string> tasks, DateTime timestamp)
        {
            // search for existing OrgFunction
            var existingFunctions = organisationFunktionStub.SoegAndGetLatestRegistration(UUIDConstants.ORGFUN_CONTACT_UNIT, null, uuid, null);

            // if no function exists, and tasks is empty, do nothing
            if ((existingFunctions == null || existingFunctions.Count == 0) && (tasks == null || tasks.Count == 0))
            {
                return;
            }

            if (existingFunctions == null || existingFunctions.Count == 0)
            {
                // create
                organisationFunktionStub.Importer(new OrgFunctionData()
                {
                    FunctionTypeUuid = UUIDConstants.ORGFUN_CONTACT_UNIT,
                    Name = "Henvendelssted",
                    OrgUnits = new List<string>() { uuid },
                    Tasks = tasks
                });
            }
            else
            {
                var existingFunction = existingFunctions[0];

                // update
                organisationFunktionStub.Ret(new OrgFunctionData()
                {
                    Uuid = existingFunction.ObjektType.UUIDIdentifikator,
                    Tasks = tasks,
                    Timestamp = timestamp
                }, UpdateIndicator.NONE, UpdateIndicator.NONE, UpdateIndicator.COMPARE);
            }
        }
    }
}
