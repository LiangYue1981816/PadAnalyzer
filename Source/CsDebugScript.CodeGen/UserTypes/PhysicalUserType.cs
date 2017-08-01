﻿using CsDebugScript.CodeGen.TypeTrees;
using Dia2Lib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CsDebugScript.CodeGen.UserTypes
{
    /// <summary>
    /// Helper functions for string manipulation.
    /// </summary>
    internal static class StringExtensions
    {
        /// <summary>
        /// Makes first letter in string uppercase.
        /// </summary>
        /// <param name="s">The input string.</param>
        /// <returns>String that has first letter uppercase.</returns>
        public static string UppercaseFirst(this string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            char[] a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }
    }

    /// <summary>
    /// Physical representation of the user type
    /// </summary>
    /// <seealso cref="UserType" />
    internal class PhysicalUserType : UserType
    {
        /// <summary>
        /// The constant for ClassCodeType
        /// </summary>
        private const string ClassCodeType = "ClassCodeType";

        /// <summary>
        /// The additionally generated field types (cached for further use and removing querying module cache of code types)
        /// </summary>
        private readonly Dictionary<string, string> addedFieldTypes = new Dictionary<string, string>();

        /// <summary>
        /// The base class offset
        /// </summary>
        private int baseClassOffset = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhysicalUserType"/> class.
        /// </summary>
        /// <param name="symbol">The symbol we are generating this user type from.</param>
        /// <param name="xmlType">The XML description of the type.</param>
        /// <param name="nameSpace">The namespace it belongs to.</param>
        public PhysicalUserType(Symbol symbol, XmlType xmlType, string nameSpace)
            : base(symbol, xmlType, nameSpace)
        {
        }

        /// <summary>
        /// Gets the type tree for the base class.
        /// If class has multi inheritance, it can return MultiClassInheritanceTypeTree or SingleClassInheritanceWithInterfacesTypeTree.
        /// </summary>
        /// <param name="error">The error text writer.</param>
        /// <param name="type">The type for which we are getting base class.</param>
        /// <param name="factory">The user type factory.</param>
        /// <param name="baseClassOffset">The base class offset.</param>
        protected override TypeTree GetBaseClassTypeTree(TextWriter error, Symbol type, UserTypeFactory factory, out int baseClassOffset)
        {
            TypeTree baseType = base.GetBaseClassTypeTree(error, type, factory, out baseClassOffset);

            this.baseClassOffset = baseClassOffset;
            return baseType;
        }

        /// <summary>
        /// Gets the automatically generated fields.
        /// </summary>
        /// <param name="hasNonStatic">if set to <c>true</c> this class has dynamic fields.</param>
        /// <param name="useThisClass">if set to <c>true</c> this class is using thisClass variable.</param>
        /// <returns>The automatically generated fields.</returns>
        protected override IEnumerable<UserTypeField> GetAutoGeneratedFields(bool hasNonStatic, bool useThisClass)
        {
            if (useThisClass)
            {
                yield return new UserTypeField
                {
                    ConstructorText = string.Format("GetBaseClass(baseClassString)"),
                    FieldName = "thisClass",
                    FieldType = "Variable",
                    FieldTypeInfoComment = null,
                    PropertyName = null,
                    Static = false,
                    UseUserMember = true,
                    CacheResult = true,
                };
            }

            if (IsTypeUsingStaticCodeType(this))
                yield return new UserTypeField
                {
                    Access = "public",
                    ConstructorText = string.Format("CodeType.Create({0})", GlobalCache.GenerateClassCodeTypeInfo(Symbol, TypeName)),
                    FieldName = ClassCodeType,
                    FieldType = "CodeType",
                    FieldTypeInfoComment = null,
                    PropertyName = null,
                    Static = true,
                    UseUserMember = false,
                    CacheResult = true,
                };

            yield return new UserTypeField
            {
                ConstructorText = string.Format("GetBaseClassString(typeof({0}))", FullClassName),
                FieldName = "baseClassString",
                FieldType = "string",
                FieldTypeInfoComment = null,
                PropertyName = null,
                Static = true,
                UseUserMember = false,
                CacheResult = true,
            };

            yield return new UserTypeFunction
            {
                FieldName = "PartialInitialize",
                FieldType = "partial void",
                CacheResult = true,
            };

            if (baseClassOffset < 0)
            {
                throw new NotImplementedException();
            }

            // If base class offset is not 0, we have generated variables for getting memory bufffer
            if (baseClassOffset > 0)
            {
                yield return new UserTypeField
                {
                    Access = "protected",
                    ConstructorText = "buffer",
                    FieldName = "memoryBuffer",
                    FieldType = "CsDebugScript.Engine.Utility.MemoryBuffer",
                    FieldTypeInfoComment = null,
                    OverrideWithNew = true,
                    PropertyName = null,
                    Static = false,
                    UseUserMember = false,
                    CacheResult = true,
                };

                yield return new UserTypeField
                {
                    Access = "protected",
                    ConstructorText = "offset",
                    FieldName = "memoryBufferOffset",
                    FieldType = "int",
                    FieldTypeInfoComment = null,
                    OverrideWithNew = true,
                    PropertyName = null,
                    Static = false,
                    UseUserMember = false,
                    CacheResult = true,
                };

                yield return new UserTypeField
                {
                    Access = "protected",
                    ConstructorText = "bufferAddress",
                    FieldName = "memoryBufferAddress",
                    FieldType = "ulong",
                    FieldTypeInfoComment = null,
                    OverrideWithNew = true,
                    PropertyName = null,
                    Static = false,
                    UseUserMember = false,
                    CacheResult = true,
                };
            }

            foreach (var addedType in addedFieldTypes)
                yield return new UserTypeField
                {
                    ConstructorText = string.Format("{0}.GetClassFieldType(\"{1}\")", ClassCodeType, addedType.Key),
                    FieldName = addedType.Value,
                    FieldType = "CodeType",
                    FieldTypeInfoComment = null,
                    PropertyName = null,
                    Static = true,
                    UseUserMember = false,
                    CacheResult = true,
                };
        }

        /// <summary>
        /// Generates the constructors.
        /// </summary>
        /// <param name="generationFlags">The user type generation flags.</param>
        protected override IEnumerable<UserTypeConstructor> GenerateConstructors(UserTypeGenerationFlags generationFlags)
        {
            yield return new UserTypeConstructor()
            {
                ContainsFieldDefinitions = true,
                Static = true,
            };

            yield return new UserTypeConstructor()
            {
                Arguments = "Variable variable",
                BaseClassInitialization = string.Format("this(variable.GetBaseClass(baseClassString), Debugger.ReadMemory(variable.GetCodeType().Module.Process, variable.GetBaseClass(baseClassString).GetPointerAddress(), {0}), 0, variable.GetBaseClass(baseClassString).GetPointerAddress())", Symbol.Size),
                ContainsFieldDefinitions = false,
                Static = false,
            };

            yield return new UserTypeConstructor()
            {
                Arguments = "Variable variable, CsDebugScript.Engine.Utility.MemoryBuffer buffer, int offset, ulong bufferAddress",
                BaseClassInitialization = string.Format("base(variable, buffer, offset{0}, bufferAddress)", baseClassOffset > 0 ? " + " + baseClassOffset : ""),
                ContainsFieldDefinitions = true,
                Static = false,
            };

            yield return new UserTypeConstructor()
            {
                Arguments = "CsDebugScript.Engine.Utility.MemoryBuffer buffer, int offset, ulong bufferAddress, CodeType codeType, ulong address, string name = Variable.ComputedName, string path = Variable.UnknownPath",
                BaseClassInitialization = string.Format("base(buffer, offset{0}, bufferAddress, codeType, address, name, path)", baseClassOffset > 0 ? " + " + baseClassOffset : ""),
                ContainsFieldDefinitions = true,
                Static = false,
            };
        }

        /// <summary>
        /// Generates user type field based on the specified symbol field and all other fields that are prepared for this function.
        /// Do not use this function directly, unless you are calling it from overridden function.
        /// </summary>
        /// <param name="field">The symbol field.</param>
        /// <param name="fieldType">The field tree type.</param>
        /// <param name="factory">The user type factory.</param>
        /// <param name="simpleFieldValue">The code foe "simple field value" used when creating transformation.</param>
        /// <param name="gettingField">The code for getting field variable.</param>
        /// <param name="isStatic">if set to <c>true</c> generated field should be static.</param>
        /// <param name="generationFlags">The user type generation flags.</param>
        /// <param name="extractingBaseClass">if set to <c>true</c> user type field is being generated for getting base class.</param>
        protected override UserTypeField ExtractFieldInternal(SymbolField field, TypeTree fieldType, UserTypeFactory factory, string simpleFieldValue, string gettingField, bool isStatic, UserTypeGenerationFlags generationFlags, bool extractingBaseClass)
        {
            // Physical code generation make sense only for non-static fields
            if (!isStatic)
            {
                bool lazyCacheUserTypeFields = generationFlags.HasFlag(UserTypeGenerationFlags.LazyCacheUserTypeFields);
                bool cacheUserTypeFields = generationFlags.HasFlag(UserTypeGenerationFlags.CacheUserTypeFields);
                bool cacheStaticUserTypeFields = generationFlags.HasFlag(UserTypeGenerationFlags.CacheStaticUserTypeFields);
                string constructorText = "";
                string fieldName = field.Name;
                string fieldTypeString = fieldType.GetTypeString();
                BasicTypeTree baseType = fieldType as BasicTypeTree;
                ArrayTypeTree codeArrayType = fieldType as ArrayTypeTree;
                UserTypeTree userType = fieldType as UserTypeTree;
                TransformationTypeTree transformationType = fieldType as TransformationTypeTree;
                bool isEmbedded = field.Type.Tag != SymTagEnum.SymTagPointerType;

                // Specialization for basic types
                if (baseType != null)
                {
                    if (baseType.BasicType == "string")
                    {
                        int charSize = field.Type.ElementType.Size;

                        constructorText = string.Format("ReadString(GetCodeType().Module.Process, ReadPointer(memoryBuffer, memoryBufferOffset + {0}, {1}), {2})", field.Offset, field.Type.Size, charSize);
                    }
                    else if (baseType.BasicType != "NakedPointer")
                    {
                        if (field.LocationType == LocationType.BitField)
                            constructorText = string.Format("Read{0}(memoryBuffer, memoryBufferOffset + {1}, {2}, {3})", baseType.GetTypeString().UppercaseFirst(), field.Offset, field.Size, field.BitPosition);
                        else
                            constructorText = string.Format("Read{0}(memoryBuffer, memoryBufferOffset + {1})", baseType.GetTypeString().UppercaseFirst(), field.Offset);
                    }
                }
                // Specialization for arrays
                else if (codeArrayType != null)
                {
                    if (codeArrayType.ElementType is BasicTypeTree)
                    {
                        baseType = (BasicTypeTree)codeArrayType.ElementType;
                        if (baseType != null && baseType.BasicType != "string" && baseType.BasicType != "NakedPointer")
                        {
                            int arraySize = field.Type.Size;
                            int elementSize = field.Type.ElementType.Size;

                            if (baseType.BasicType == "char")
                                constructorText = string.Format("Read{0}Array(memoryBuffer, memoryBufferOffset + {1}, {2}, {3})", baseType.GetTypeString().UppercaseFirst(), field.Offset, arraySize / elementSize, elementSize);
                            else
                                constructorText = string.Format("Read{0}Array(memoryBuffer, memoryBufferOffset + {1}, {2})", baseType.GetTypeString().UppercaseFirst(), field.Offset, arraySize / elementSize);
                            fieldTypeString = baseType.GetTypeString() + "[]";
                        }
                    }
                }
                // Specialization for user types
                else if (userType != null && !extractingBaseClass)
                {
                    if (!(userType.UserType is EnumUserType))
                    {
                        string thisClassCodeType;

                        if (IsTypeUsingStaticCodeType(this))
                            thisClassCodeType = ClassCodeType;
                        else
                        {
                            thisClassCodeType = "thisClass.Value.GetCodeType()";
                            usedThisClass = true;
                        }

                        // Check if type is embedded
                        if (!isEmbedded)
                        {
                            // If user type is not embedded, we do have pointer inside of our memory buffer that we can read directly
                            if (IsTypeUsingStaticCodeType(this))
                                constructorText = string.Format("ReadPointer<{0}>({4}, \"{1}\", memoryBuffer, memoryBufferOffset + {2}, {3})", fieldTypeString, fieldName, field.Offset, field.Type.Size, ClassCodeType);
                            else
                            {
                                constructorText = string.Format("ReadPointer<{0}>(thisClass, \"{1}\", memoryBuffer, memoryBufferOffset + {2}, {3})", fieldTypeString, fieldName, field.Offset, field.Type.Size);
                                usedThisClass = true;
                            }

                            // Do downcasting if field has vtable
                            if (userType.UserType.Symbol.HasVTable() && userType.UserType.DerivedClasses.Count > 0)
                                constructorText += ".DowncastObject()";
                        }
                        else
                        {
                            // If user type is embedded, we can reuse memory buffer that we already have in this class
                            string fieldAddress = string.Format("memoryBufferAddress + (ulong)(memoryBufferOffset + {0})", field.Offset);
                            string fieldCodeType = string.Format("{0}.GetClassFieldType(\"{1}\")", thisClassCodeType, fieldName);

                            if (IsTypeUsingStaticCodeType(userType.UserType))
                            {
                                fieldCodeType = string.Format("{0}.{1}", userType.UserType.FullClassName, ClassCodeType);
                            }
                            else if (IsTypeUsingStaticCodeType(this))
                            {
                                fieldCodeType = AddFieldCodeType(fieldName);
                            }

                            constructorText = string.Format("new {0}(memoryBuffer, memoryBufferOffset + {1}, memoryBufferAddress, {2}, {3}, \"{4}\")", fieldTypeString, field.Offset, fieldCodeType, fieldAddress, fieldName);
                        }
                    }
                    else
                    {
                        // TODO: This is enum. Read how much enum base type is big and just cast to enum type...
                    }
                }
                // Specialization for transformations
                else if (transformationType != null)
                {
                    if (!isEmbedded)
                    {
                        string thisClassCodeType;

                        if (IsTypeUsingStaticCodeType(this))
                            thisClassCodeType = ClassCodeType;
                        else
                        {
                            thisClassCodeType = "thisClass.Value.GetCodeType()";
                            usedThisClass = true;
                        }

                        string fieldAddress = string.Format("memoryBufferAddress + (ulong)(memoryBufferOffset + {0})", field.Offset);
                        string fieldVariable = string.Format("Variable.CreateNoCast({0}.GetClassFieldType(\"{1}\"), {2}, \"{1}\")", thisClassCodeType, fieldName, fieldAddress);

                        if (transformationType.Transformation.Transformation.HasPhysicalConstructor)
                        {
                            fieldVariable = string.Format("{0}, memoryBuffer, memoryBufferOffset + {1}, memoryBufferAddress", fieldVariable, field.Offset);
                        }

                        simpleFieldValue = fieldVariable;
                        constructorText = string.Format("new {0}({1})", fieldTypeString, fieldVariable);
                    }
                }

                // If we found suitable physical representation, generate the field
                if (!string.IsNullOrEmpty(constructorText))
                    return new UserTypeField()
                    {
                        ConstructorText = constructorText,
                        FieldName = "_" + fieldName,
                        FieldType = fieldTypeString,
                        FieldTypeInfoComment = string.Format("// {0} {1};", field.Type.Name, fieldName),
                        PropertyName = UserTypeField.GetPropertyName(field, this),
                        Static = isStatic,
                        UseUserMember = lazyCacheUserTypeFields,
                        CacheResult = cacheUserTypeFields || (isStatic && cacheStaticUserTypeFields),
                        SimpleFieldValue = simpleFieldValue,
                    };
            }

            return base.ExtractFieldInternal(field, fieldType, factory, simpleFieldValue, gettingField, isStatic, generationFlags, extractingBaseClass);
        }

        /// <summary>
        /// Determines whether the specified user type has defined static variable for class code type.
        /// </summary>
        /// <param name="userType">The user type.</param>
        private static bool IsTypeUsingStaticCodeType(UserType userType)
        {
            // Only physical user type has this defined
            if (!(userType is PhysicalUserType))
                return false;

            // If user type is declared inside the template user type, it doesn't have variable for class code type
            while (userType != null)
            {
                if (userType is TemplateUserType)
                    return false;
                userType = userType.DeclaredInType;
            }

            return true;
        }

        /// <summary>
        /// Add field to the list of additionally generated field types.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <returns>Generated name for the variable that will cache code type for the field.</returns>
        private string AddFieldCodeType(string fieldName)
        {
            string newType = fieldName + "ↀ";

            addedFieldTypes.Add(fieldName, newType);
            return newType;
        }
    }
}
