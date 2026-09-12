// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Used to communicate between scripting and an <see cref="T:UnityEditor.Animations.AnimatorController" />.</summary>
    ///<remarks>
    ///  <para>You can add an <see cref="AnimatorControllerParameter" /> to an <see cref="T:UnityEditor.Animations.AnimatorController" /> in the Animator window or with the function <see cref="M:UnityEditor.Animations.AnimatorController.AddParameter" /> in script. At runtime, use the following functions to set the value of a parameter in the <see cref="Animator" />:
    ///
    ///* <see cref="Animator.SetBool" />
    ///* <see cref="Animator.SetFloat" />
    ///* <see cref="Animator.SetInteger" />
    ///* <see cref="Animator.SetTrigger" />
    ///
    ///You can also set parameter values in the Animation window based on Animation Curves in Animation Clips.</para>
    ///  <para />
    ///</remarks>
    ///<example nocheck="true">
    ///  <code><![CDATA[{code Tests/EditModeAndPlayModeTests/Animation/Assets/Editor/DocumentationExamples/AnimatorControllerParameterExample.cs}]]></code>
    ///</example>
    ///<seealso href="xref:AnimationParameters" />
    ///<seealso cref="M:UnityEditor.Animations.AnimatorController.RemoveParameter" />
    ///<seealso cref="P:UnityEditor.Animations.AnimatorController.parameters" />
    [NativeHeader("Modules/Animation/AnimatorControllerParameter.h")]
    [NativeAsStruct]
    [UsedByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    public class AnimatorControllerParameter
    {
        ///<summary>The name of the parameter.</summary>
        public string                             name
        {
            get { return m_Name; }
            set {   m_Name = value;     }
        }

        ///<summary>Returns the hash of the parameter based on its name.</summary>
        ///<seealso cref="Animator.StringToHash" />
        public int                                nameHash
        {
            get { return Animator.StringToHash(m_Name); }
        }

        ///<summary>The type of the parameter.</summary>
        public AnimatorControllerParameterType    type                            {   get { return m_Type; }                      set {  m_Type = value; } }
        ///<summary>The default float value for the parameter.</summary>
        ///<remarks>Only valid for Parameters of type <see cref="AnimatorControllerParameterType.Float" />.</remarks>
        public float                              defaultFloat                    {   get { return m_DefaultFloat; }              set {  m_DefaultFloat = value; } }
        ///<summary>The default int value for the parameter.</summary>
        ///<remarks>Only valid for Parameters of type <see cref="AnimatorControllerParameterType.Int" />.</remarks>
        public int                                defaultInt                      {   get { return m_DefaultInt; }                set {  m_DefaultInt = value; }   }
        ///<summary>The default bool value for the parameter.</summary>
        ///<remarks>Only valid for Parameters of type <see cref="AnimatorControllerParameterType.Bool" />.</remarks>
        public bool                               defaultBool                     {   get { return m_DefaultBool; }               set {  m_DefaultBool = value; }  }

        internal string                                 m_Name = "";
        internal AnimatorControllerParameterType        m_Type;
        internal float                                  m_DefaultFloat;
        internal int                                    m_DefaultInt;
        internal bool                                   m_DefaultBool;

        ///<exclude />
        public override bool Equals(object o)
        {
            AnimatorControllerParameter other = o as AnimatorControllerParameter;
            return other != null && m_Name == other.m_Name && m_Type == other.m_Type && m_DefaultFloat == other.m_DefaultFloat && m_DefaultInt == other.m_DefaultInt && m_DefaultBool == other.m_DefaultBool;
        }

        ///<exclude />
        public override int GetHashCode()
        {
            return name.GetHashCode();
        }
    }
}
