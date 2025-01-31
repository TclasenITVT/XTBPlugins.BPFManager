﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;

namespace Carfup.XTBPlugins.AppCode
{
    public class DataManager
    {
        public CrmServiceClient service { get; set; } = null;
        public int recordToRetrieveEachRound = 5000;
        public EntityMetadata[] entitiesMetadata = null;

        public DataManager(CrmServiceClient service)
        {
            this.service = service;
        }

        public List<Entity> GetRecordsToMigrate(string fetchXmlQuery, BackgroundWorker worker = null)
        {
            List<Entity> recordToMigrate = new List<Entity>();
            QueryExpression query = ConvertFetchXMLtoQueryExpression(fetchXmlQuery);
            query.NoLock = true;

            if(query.TopCount == null)
            {
                query.PageInfo = new PagingInfo
                {
                    Count = recordToRetrieveEachRound,
                    PageNumber = 1
                };
            }


            EntityCollection ec;
            int total = 0;
            do
            {
                ec = service.RetrieveMultiple(query);
                total += ec.Entities.Count;
                if (query.TopCount == null)
                {
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = ec.PagingCookie;
                }

                worker?.ReportProgress(0, $"{total} records retrieved...");


                recordToMigrate.AddRange(ec.Entities);
                //foreach (var record in ec.Entities)
                //{
                //    recordToMigrate.Add(record);

                //}
            } while (ec.MoreRecords /*&& Cancel == false*/);

            //var result = this.service.RetrieveMultiple(query);

            return recordToMigrate;
        }

        public List<Entity> GetUsers(BackgroundWorker worker = null)
        {
            List<Entity> users = new List<Entity>();
            var query = new QueryExpression()
            {
                EntityName = "systemuser",
                ColumnSet = new ColumnSet("fullname"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("isdisabled", ConditionOperator.Equal, "0"),
                        new ConditionExpression("lastname", ConditionOperator.NotIn,
                            new string[] {"SYSTEM", "INTEGRATION"}),
                        new ConditionExpression("domainname", ConditionOperator.NotNull),
                        new ConditionExpression("domainname", ConditionOperator.NotEqual, ""),
                        new ConditionExpression("domainname", ConditionOperator.NotEqual, "bap_sa@microsoft.com"),
                        new ConditionExpression("accessmode", ConditionOperator.NotIn, new string[] {"3", "5"}),
                    }
                },
                PageInfo =
                {
                    Count = recordToRetrieveEachRound,
                    PageNumber = 1
                },
                NoLock = true
            };


            EntityCollection ec;
            int total = 0;
            do
            {
                ec = service.RetrieveMultiple(query);
                total += ec.Entities.Count;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;

                worker?.ReportProgress(0, $"{total} records retrieved...");

                users.AddRange(ec.Entities);
                
            } while (ec.MoreRecords /*&& Cancel == false*/);

            return users;
        }

        public List<Entity> GetProperBPFList(string bpfSelectedEntityTarget, List<Entity> recordToMigrateList, string attrForCondition)
        {
            List<Entity> resultQueryProperBPF = new List<Entity>();

            var query = new QueryExpression()
            {
                EntityName = bpfSelectedEntityTarget,
                ColumnSet = new ColumnSet(true),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(attrForCondition, ConditionOperator.In,
                            recordToMigrateList.Select(x => x.Id as object).ToArray<object>())
                    }
                },
                PageInfo =
                {
                    Count = recordToRetrieveEachRound,
                    PageNumber = 1
                },
                NoLock = true
            };
            

            EntityCollection ec;
            int total = 0;
            do
            {
                try
                {
                    ec = service.RetrieveMultiple(query);
                    total += ec.Entities.Count;
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = ec.PagingCookie;

                    resultQueryProperBPF.AddRange(ec.Entities);
                }
                catch (FaultException<OrganizationServiceFault> ex)
                {
                    if(ex.Message.Contains("is missing prv"))
                        throw new Exception($"One or more users are unable to access the Target BPF.{Environment.NewLine}Ensure permissions are set before proceeding.{Environment.NewLine}{Environment.NewLine}Would you like to proceed ?");

                    throw;
                }
                ec = service.RetrieveMultiple(query);
                total += ec.Entities.Count;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;

                resultQueryProperBPF.AddRange(ec.Entities);

            } while (ec.MoreRecords /*&& Cancel == false*/);

            return resultQueryProperBPF;
        }

        public List<Entity> GetRelatedBPF(string recordEntityToMigrate)
        {
            return this.service.RetrieveMultiple(new QueryExpression()
            {
                EntityName = "workflow",
                ColumnSet = new ColumnSet("name","uniquename"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("category", ConditionOperator.Equal, 4),
                        new ConditionExpression("primaryentity", ConditionOperator.Equal, recordEntityToMigrate)
                    }
                }
            }).Entities.ToList();
        }

        public List<Entity> GetExistingBPFInstances(string bpfEntityName, string relatedEntityName, Guid[] guidList)
        {
            var bpfMetadata = GetAttributeOfEntity(bpfEntityName);
            
            var relatedEntityNameModified = $"{relatedEntityName}id";

            if(bpfMetadata.Attributes.FirstOrDefault(x => x.LogicalName == relatedEntityNameModified) == null)
                relatedEntityNameModified = $"bpf_{relatedEntityName}id";

            if (bpfMetadata.Attributes.FirstOrDefault(x => x.LogicalName == relatedEntityNameModified) == null)
                throw new Exception("We couldn't figure out what is the proper primary fieldname of the BPF entity.");

                return this.service.RetrieveMultiple(new QueryExpression()
            {
                EntityName = bpfEntityName,
                ColumnSet = new ColumnSet("businessprocessflowinstanceid", relatedEntityNameModified),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(relatedEntityNameModified, ConditionOperator.In, guidList),
                    }
                }
            }).Entities.ToList();
        }

        public List<Entity> GetBPFStages(Guid bpfSelected)
        {
            return this.service.RetrieveMultiple(new QueryExpression()
            {
                EntityName = "processstage",
                ColumnSet = new ColumnSet(true),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("processid", ConditionOperator.Equal, bpfSelected)
                    }
                }
            }).Entities.ToList();
        }

        public List<EntityDetailledName> GetEntitiesWithBPF()
        {
            if (entitiesMetadata == null)
                RetrieveMetadataEntity();

            var query = new QueryExpression()
            {
                EntityName = "workflow",
                ColumnSet = new ColumnSet("primaryentity"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("category", ConditionOperator.Equal, 4),
                    }
                }
            };

            var result = this.service.RetrieveMultiple(query).Entities;

            List<EntityDetailledName> edn = new List<EntityDetailledName>();
            foreach (var r in result.GroupBy(x => x.Attributes["primaryentity"]).Select(w => (string)w.Key))
            {
                edn.Add(new EntityDetailledName()
                {
                    logicalName = r,
                    schemaName = entitiesMetadata.FirstOrDefault(x => x.LogicalName == r)?.SchemaName,
                    displayName = entitiesMetadata.FirstOrDefault(x => x.LogicalName == r)?.DisplayName.UserLocalizedLabel.Label
                });
            }

            return edn; //result.GroupBy(x => x.Attributes["primaryentity"]).Select(w => (string)w.Key).ToArray();
        }

        public List<Entity> GetSystemViewsOfEntity(string entity)
        {
            var querySystemViews = this.service.RetrieveMultiple(new QueryExpression()
            {
                EntityName = "savedquery",
                ColumnSet = new ColumnSet("name", "fetchxml"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("returnedtypecode", ConditionOperator.Equal, entity)
                    }
                }
            });

            return querySystemViews.Entities.ToList();
        }

        public List<Entity> GetPersonalViewsOfEntity(string entity)
        {
            var queryUserViews = this.service.RetrieveMultiple(new QueryExpression()
            {
                EntityName = "userquery",
                ColumnSet = new ColumnSet("name", "fetchxml"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("returnedtypecode", ConditionOperator.Equal, entity)
                    }
                }
            });

            return queryUserViews.Entities.ToList();
        }

        private QueryExpression ConvertFetchXMLtoQueryExpression(string fetchXmlQuery)
        {
            FetchXmlToQueryExpressionRequest conversionRequest = new FetchXmlToQueryExpressionRequest
            {
                FetchXml = fetchXmlQuery
            };

            FetchXmlToQueryExpressionResponse conversionResponse =
                (FetchXmlToQueryExpressionResponse)service.Execute(conversionRequest);

            QueryExpression queryExpression = conversionResponse.Query;

            return queryExpression;
        }

        public string GetPrimaryNameAttributeOfEntity(string entity)
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Entity,
                LogicalName = entity
            };

            var metadata = (RetrieveEntityResponse)service.Execute(request);
            return metadata.EntityMetadata.PrimaryNameAttribute;
        }

        public EntityMetadata GetAttributeOfEntity(string entity)
        {
            var request = new RetrieveEntityRequest
            {
                EntityFilters = EntityFilters.Attributes,
                LogicalName = entity
            };

            var metadata = (RetrieveEntityResponse)service.Execute(request);
            return metadata.EntityMetadata;
        }

        public void RetrieveMetadataEntity()
        {
            RetrieveAllEntitiesRequest request = new RetrieveAllEntitiesRequest()
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = true
            };

            // Retrieve the MetaData.
            RetrieveAllEntitiesResponse response = (RetrieveAllEntitiesResponse)this.service.Execute(request);

            entitiesMetadata = response.EntityMetadata;
        }

        public string RetrieveReferencingAttributeOfBpf(string bfpEntityName, string recordEntityName)
        {
            var requestMetadataBpfEntity = new RetrieveEntityRequest()
            {
                EntityFilters = EntityFilters.Relationships,
                LogicalName = bfpEntityName
            };

            var responseMetadataBpfEntity = (RetrieveEntityResponse)this.service.Execute(requestMetadataBpfEntity);
            return responseMetadataBpfEntity.EntityMetadata.ManyToOneRelationships.FirstOrDefault(x => x.ReferencedEntity == recordEntityName).ReferencingAttribute;
        }

        public Entity GetExistingBpfInstance(string bpfEntityName, string referencingAttribute, Guid record) 
        {
            var queryUserViews = this.service.RetrieveMultiple(new QueryExpression()
            {
                EntityName = bpfEntityName,
                ColumnSet = new ColumnSet(false),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(referencingAttribute, ConditionOperator.Equal, record)
                    }
                }
            });

            return queryUserViews.Entities.FirstOrDefault();
        }
    }

    public class EntityDetailledName
    {
        public string logicalName { get; set; }
        public string schemaName { get; set; }
        public string displayName { get; set; }
    }
}
