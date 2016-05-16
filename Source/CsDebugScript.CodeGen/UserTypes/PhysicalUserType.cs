﻿using Dia2Lib;
using System.Collections.Generic;
using System;
using System.IO;

namespace CsDebugScript.CodeGen.UserTypes
{
    class PhysicalUserType : UserType
    {
        private const string ClassCodeType = "ClassCodeType";
        private readonly Dictionary<string, string> addedFieldTypes = new Dictionary<string, string>();
        private int baseClassOffset = -1;

        public PhysicalUserType(Symbol symbol, XmlType xmlType, string nameSpace)
            : base(symbol, xmlType, nameSpace)
        {
        }

        protected override UserTypeTree GetBaseTypeString(TextWriter error, Symbol type, UserTypeFactory factory, out int baseClassOffset)
        {
            UserTypeTree baseType = base.GetBaseTypeString(error, type, factory, out baseClassOffset);

            this.baseClassOffset = baseClassOffset;
            return baseType;
        }

        protected override IEnumerable<UserTypeField> GetAutoGeneratedFields(bool hasNonStatic, bool useThisClass)
        {
            if (hasNonStatic && useThisClass)
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
                    ConstructorText = string.Format("CodeType.Create({0})", GenerateClassCodeTypeInfo()),
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

        protected override IEnumerable<UserTypeConstructor> GenerateConstructors()
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

        protected override UserTypeField ExtractField(SymbolField field, UserTypeTree fieldType, UserTypeFactory factory, string simpleFieldValue, string gettingField, bool isStatic, UserTypeGenerationFlags options, bool extractingBaseClass)
        {
            if (!isStatic)
            {
                bool lazyCacheUserTypeFields = options.HasFlag(UserTypeGenerationFlags.LazyCacheUserTypeFields);
                bool cacheUserTypeFields = options.HasFlag(UserTypeGenerationFlags.CacheUserTypeFields);
                bool cacheStaticUserTypeFields = options.HasFlag(UserTypeGenerationFlags.CacheStaticUserTypeFields);
                string constructorText = "";
                string fieldName = field.Name;
                string fieldTypeString = fieldType.GetUserTypeString();
                UserTypeTreeBaseType baseType = fieldType as UserTypeTreeBaseType;
                UserTypeTreeCodeArray codeArrayType = fieldType as UserTypeTreeCodeArray;
                UserTypeTreeUserType userType = fieldType as UserTypeTreeUserType;
                UserTypeTreeTransformation transformationType = fieldType as UserTypeTreeTransformation;
                bool isEmbedded = field.Type.Tag != SymTagEnum.SymTagPointerType;

                if (baseType != null)
                {
                    if (baseType.BaseType == "string")
                    {
                        int charSize = field.Type.ElementType.Size;

                        constructorText = string.Format("ReadString(GetCodeType().Module.Process, ReadPointer(memoryBuffer, memoryBufferOffset + {0}, {1}), {2})", field.Offset, field.Type.Size, charSize);
                    }
                    else if (baseType.BaseType != "NakedPointer")
                    {
                        if (field.LocationType == LocationType.BitField)
                            constructorText = string.Format("Read{0}(memoryBuffer, memoryBufferOffset + {1}, {2}, {3})", baseType.GetUserTypeString().UppercaseFirst(), field.Offset, field.Size, field.BitPosition);
                        else
                            constructorText = string.Format("Read{0}(memoryBuffer, memoryBufferOffset + {1})", baseType.GetUserTypeString().UppercaseFirst(), field.Offset);
                    }
                }
                else if (codeArrayType != null)
                {
                    if (codeArrayType.InnerType is UserTypeTreeBaseType)
                    {
                        baseType = (UserTypeTreeBaseType)codeArrayType.InnerType;
                        if (baseType != null && baseType.BaseType != "string" && baseType.BaseType != "NakedPointer")
                        {
                            int arraySize = field.Type.Size;
                            int elementSize = field.Type.ElementType.Size;

                            if (baseType.BaseType == "char")
                                constructorText = string.Format("Read{0}Array(memoryBuffer, memoryBufferOffset + {1}, {2}, {3})", baseType.GetUserTypeString().UppercaseFirst(), field.Offset, arraySize / elementSize, elementSize);
                            else
                                constructorText = string.Format("Read{0}Array(memoryBuffer, memoryBufferOffset + {1}, {2})", baseType.GetUserTypeString().UppercaseFirst(), field.Offset, arraySize / elementSize);
                            fieldTypeString = baseType.GetUserTypeString() + "[]";
                        }
                    }
                }
                else if (userType != null)
                {
                    if (!(userType.UserType is EnumUserType) && !extractingBaseClass)
                    {
                        string thisClassCodeType;

                        if (IsTypeUsingStaticCodeType(this))
                            thisClassCodeType = ClassCodeType;
                        else
                        {
                            thisClassCodeType = "thisClass.Value.GetCodeType()";
                            usedThisClass = true;
                        }

                        if (!isEmbedded)
                        {
                            if (IsTypeUsingStaticCodeType(this))
                                constructorText = string.Format("ReadPointer<{0}>({4}, \"{1}\", memoryBuffer, memoryBufferOffset + {2}, {3})", fieldTypeString, fieldName, field.Offset, field.Type.Size, ClassCodeType);
                            else
                            {
                                constructorText = string.Format("ReadPointer<{0}>(thisClass, \"{1}\", memoryBuffer, memoryBufferOffset + {2}, {3})", fieldTypeString, fieldName, field.Offset, field.Type.Size);
                                usedThisClass = true;
                            }
                        }
                        else
                        {
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
                }
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

                if (!string.IsNullOrEmpty(constructorText))
                    return new UserTypeField()
                    {
                        ConstructorText = constructorText,
                        FieldName = "_" + fieldName,
                        FieldType = fieldTypeString,
                        FieldTypeInfoComment = string.Format("// {0} {1};", field.Type.Name, fieldName),
                        PropertyName = UserTypeField.GetPropertyName(fieldName, this),
                        Static = isStatic,
                        UseUserMember = lazyCacheUserTypeFields,
                        CacheResult = cacheUserTypeFields || (isStatic && cacheStaticUserTypeFields),
                        SimpleFieldValue = simpleFieldValue,
                    };
            }

            return base.ExtractField(field, fieldType, factory, simpleFieldValue, gettingField, isStatic, options, extractingBaseClass);
        }

        private static bool IsTypeUsingStaticCodeType(UserType userType)
        {
            if (!(userType is PhysicalUserType))
                return false;

            while (userType != null)
            {
                if (userType is TemplateUserType)
                    return false;
                userType = userType.DeclaredInType;
            }

            return true;
        }

        private string AddFieldCodeType(string fieldName)
        {
            string newType = fieldName + "ↀ";

            addedFieldTypes.Add(fieldName, newType);
            return newType;
        }
    }
}
