﻿using AweCsomeO365.Attributes;
using AweCsomeO365.Attributes.FieldAttributes;
using AweCsomeO365.Attributes.TableAttributes;
using log4net;
using Microsoft.SharePoint.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AweCsomeO365
{
    public static class EntityHelper
    {
        private static ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);


        private static void RemoveSuffixFromName(ref string name, string suffix)
        {
            if (name == null) return;
            if (name.EndsWith(suffix)) name = name.Substring(0, name.Length - suffix.Length);
        }

        public static void RemoveLookupIdFromFieldName(bool isArray, ref string internalName, ref string displayName)
        {
            RemoveSuffixFromName(ref internalName, isArray ? AweCsomeField.SuffixIds : AweCsomeField.SuffixId);
            RemoveSuffixFromName(ref displayName, isArray ? AweCsomeField.SuffixIds : AweCsomeField.SuffixId);
        }


        public static string GetInternalNameFromProperty(PropertyInfo propertyInfo)
        {
            Type propertyType = propertyInfo.PropertyType;
            var internalNameAttribute = propertyType.GetCustomAttribute<InternalNameAttribute>();
            string internalName = internalNameAttribute == null ? propertyInfo.Name : internalNameAttribute.InternalName;
            string displayName = null;
            if (PropertyIsLookup(propertyInfo)) RemoveLookupIdFromFieldName(propertyType.IsArray, ref internalName, ref displayName);
            return internalName;
        }

        public static string GetInternalNameFromEntityType(Type entityType)
        {
            var internalNameAttribute = entityType.GetCustomAttribute<InternalNameAttribute>();
            return internalNameAttribute == null ? entityType.Name : internalNameAttribute.InternalName;
        }

        public static string GetDisplayNameFromEntityType(PropertyInfo propertyInfo)
        {
            Type propertyType = propertyInfo.PropertyType;
            var displayNameAttribute = propertyType.GetCustomAttribute<DisplayNameAttribute>();
            return displayNameAttribute == null ? propertyInfo.Name : displayNameAttribute.DisplayName;
        }

        public static int GetListTemplateType(Type entityType)
        {
            var listTemplateTypeAttribute = entityType.GetCustomAttribute<ListTemplateAttribute>();
            return listTemplateTypeAttribute == null ? (int)ListTemplateType.GenericList : listTemplateTypeAttribute.TemplateTypeId;
        }

        public static string GetDescriptionFromEntityType(Type entityType)
        {
            var descriptionAttribute = entityType.GetCustomAttribute<DescriptionAttribute>();
            return descriptionAttribute?.Description;
        }

        public static bool IsGenericList(this Type type)
        {
            return (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>)));
        }

        public static bool IsDictionary(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }

        public static FieldLookupValue CreateLookupFromId(int id)
        {
            return new FieldLookupValue { LookupId = id };
        }

        public static FieldLookupValue[] CreateLookupsFromIds(int[] ids)
        {
            return ids.Select(id => new FieldLookupValue { LookupId = id }).ToArray();
        }

        public static bool PropertyIsLookup(PropertyInfo property)
        {
            if (property.GetCustomAttribute<LookupBaseAttribute>(true) != null) return true;
            Type propertyType = property.PropertyType;
            if (propertyType == typeof(KeyValuePair<int, string>)) return true; // Single-Lookup
            if (propertyType == typeof(Dictionary<int, string>)) return true; // Multi-Lookup
            if (propertyType.GetProperty(AweCsomeField.SuffixId) != null) return true; // Single Lookup with complex type
            if (propertyType.IsArray && propertyType.GetElementType().GetProperty(AweCsomeField.SuffixId) != null) return true; // Multi Lookup with complex type
            return false;
        }

        public static FieldType GetFieldType(PropertyInfo property)
        {
            if (PropertyIsLookup(property)) return FieldType.Lookup;
            Type propertyType = property.PropertyType;
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)) propertyType = propertyType.GetGenericArguments()[0];

            if (propertyType.IsArray) propertyType = propertyType.GetElementType();
            FieldType? detectedFieldType = GetFieldTypeFromAttribute(property);
            if (detectedFieldType != null) return detectedFieldType.Value;

            if (propertyType.IsEnum) return FieldType.Choice;
            switch (Type.GetTypeCode(propertyType))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return FieldType.Number;
                case TypeCode.Boolean:
                    return FieldType.Boolean;
                case TypeCode.String:
                case TypeCode.Char:
                    return FieldType.Text;
                case TypeCode.DateTime:
                    return FieldType.DateTime;

                default:
                    _log.Warn($"Cannot create fieldtype from {propertyType.Name}. Type is not supported.");
                    return FieldType.Invalid;
            }
        }

        private static FieldType? GetFieldTypeFromAttribute(PropertyInfo property)
        {
            FieldType? detectedFieldType = null;
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<BooleanAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<ChoiceAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<CurrencyAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<DateTimeAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<LookupBaseAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<ManagedMetadataAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<NoteAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<NumberAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<TextAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<Attributes.FieldAttributes.UrlAttribute>(property);
            detectedFieldType = detectedFieldType ?? GetFieldTypeByAttribute<UserAttribute>(property);
            return detectedFieldType;
        }

        private static FieldType? GetFieldTypeByAttribute<T>(PropertyInfo property) where T : Attribute
        {
            if (property.GetCustomAttribute(typeof(T), true) == null) return null;
            return (FieldType)typeof(T).GetField(nameof(BooleanAttribute.AssociatedFieldType)).GetRawConstantValue();
        }

        public static object GetItemValueForProperty(PropertyInfo property, object itemValue)
        {
            Type propertyType = property.PropertyType;
            if (PropertyIsLookup(property))
            {
                if (itemValue.GetType().IsArray)
                {
                    var fieldLookupValues = (FieldLookupValue[])itemValue;
                    if (propertyType == typeof(Dictionary<int, string>)) return fieldLookupValues.ToDictionary(q => q.LookupId, q => q.LookupValue);
                    if (propertyType == typeof(int[])) return fieldLookupValues.Select(q => q.LookupId).ToArray();
                    Type elementType = propertyType.GetElementType();

                    if (elementType.GetProperty(AweCsomeField.SuffixId) != null)
                    {
                        //var listType = typeof(List<>);
                        //var genericArgs = propertyType.GetGenericArguments();
                        //var concreteType = listType.MakeGenericType(genericArgs);
                        //var newList = Activator.CreateInstance(concreteType) as IList;

                        //         var objectType = propertyType.GetGenericArguments().First();
                        var newList = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType)) as IList;


                        var targetEntityObject = Activator.CreateInstance(elementType);
                        PropertyInfo idProperty = elementType.GetProperty(AweCsomeField.SuffixId);
                        PropertyInfo titleProperty = elementType.GetProperty(AweCsomeField.Title);

                        foreach (var fieldLookupValue in fieldLookupValues)
                        {
                            idProperty.SetValue(targetEntityObject, fieldLookupValue.LookupId);
                            if (titleProperty != null) titleProperty.SetValue(targetEntityObject, fieldLookupValue.LookupValue);
                            newList.Add(targetEntityObject);
                        }

                        var array = Array.CreateInstance(elementType, newList.Count);
                        newList.CopyTo(array, 0);
                        return array;
                    }
                }
                else
                {
                    var fieldLookupValue = (FieldLookupValue)itemValue;
                    int lookupId = fieldLookupValue?.LookupId ?? 0;
                    string lookupValue = fieldLookupValue?.LookupValue;
                    if (propertyType == typeof(KeyValuePair<int, string>)) return new KeyValuePair<int, string>(lookupId, lookupValue);
                    if (propertyType == typeof(int)) return lookupId;
                    if (propertyType == typeof(string)) return lookupValue;

                    if (propertyType.GetProperty(AweCsomeField.SuffixId) != null)
                    {
                        var targetEntityObject = Activator.CreateInstance(propertyType);
                        PropertyInfo idProperty = propertyType.GetProperty(AweCsomeField.SuffixId);
                        PropertyInfo titleProperty = propertyType.GetProperty(AweCsomeField.Title);

                        idProperty.SetValue(targetEntityObject, fieldLookupValue.LookupId);
                        if (titleProperty != null) titleProperty.SetValue(targetEntityObject, fieldLookupValue.LookupValue);
                        return targetEntityObject;
                    }
                }
            }
            if (propertyType.IsEnum)
            {
                return Enum.Parse(property.PropertyType, itemValue as string);
            }
            return itemValue;
        }

        public static object GetPropertyValueForItem<T>(PropertyInfo property, T entity)
        {
            Type propertyType = property.PropertyType;
            if (PropertyIsLookup(property))
            {
                if (propertyType == typeof(KeyValuePair<int, string>)) return CreateLookupFromId(((KeyValuePair<int, string>)property.GetValue(entity)).Key);
                if (propertyType == typeof(Dictionary<int, string>)) return CreateLookupsFromIds(((Dictionary<int, string>)property.GetValue(entity)).Select(q => q.Key).ToArray());
                if (propertyType.IsArray && propertyType.GetElementType().GetProperty(AweCsomeField.SuffixId) != null)
                {
                    List<int> ids = new List<int>();
                    foreach (var item in (object[])property.GetValue(entity))
                    {
                        ids.Add((int)item.GetType().GetProperty(AweCsomeField.SuffixId).GetValue(item));
                    }
                    return CreateLookupsFromIds(ids.ToArray());
                }
                if (propertyType.GetProperty(AweCsomeField.SuffixId) != null)
                {
                    var item = property.GetValue(entity);
                    return CreateLookupFromId(((int)item.GetType().GetProperty(AweCsomeField.SuffixId).GetValue(item)));
                }
            }
            if (propertyType.IsEnum)
            {
                return Enum.GetName(property.PropertyType, property.GetValue(entity));
            }
            return property.GetValue(entity);
        }
    }
}
