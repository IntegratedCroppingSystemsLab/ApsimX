using System;
using APSIM.Shared.Documentation;
using System.Collections.Generic;
using Models.Core;
using Newtonsoft.Json;
using Models.Functions;
using Models.Soils;
using Models.Soils.Arbitrator;
using Models.Soils.Nutrients;
using Models.Interfaces;
using Models.PMF.Interfaces;
using APSIM.Shared.Utilities;
using System.IO;
using System.Text;

namespace Models.PMF.Phen
{
    /// <summary>
    /// This phase goes from a start stage to an end stage and simulates time to 
    /// emergence as a function of sowing depth.  
    /// Progress toward emergence is driven by a thermal time accumulation child function.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.PropertyView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Phenology))]
    public class EmergingPhase : Model, IPhase, IPhaseWithTarget
    {
        // 1. Links
        //----------------------------------------------------------------------------------------------------------------

        [Link]
        Phenology phenology = null;

        [Link]
        Clock clock = null;

        [Link]
        Plant plant = null;

<<<<<<< HEAD
        [Link(Type = LinkType.Child, ByName = true)]
        private IFunction target = null;
=======
        [Link]
        IWeather WeatherRef = null;

        /// <summary>Depth-based seed loss curve</summary>
        [Link(Type=LinkType.Path, Path = "[Phenology].DepthLoss")]
        private LinearInterpolationFunction DepthLoss = null;

        /// <summary>Temperature-based seed loss curve</summary>
        [Link(Type=LinkType.Path, Path = "[Phenology].TemperatureLoss")]
        private LinearInterpolationFunction TemperatureLoss = null;

        /// <summary>Depth-based thermal target</summary>
        [Link(Type=LinkType.Path, Path = "[Phenology].DepthGDDTarget")]
        private LinearInterpolationFunction DepthGDDTarget = null;
>>>>>>> a58210d87 (implement improved emergence model)

        // 2. Public properties
        //-----------------------------------------------------------------------------------------------------------------

        /// <summary>The phenological stage at the start of this phase.</summary>
        [Description("Start")]
        public string Start { get; set; }

        /// <summary>The phenological stage at the end of this phase.</summary>
        [Models.Core.Description("End")]
        public string End { get; set; }

        /// <summary>Weight of depth-treatment-based thermal target vs. old shootlag/shootrate </summary>
        [Description("DepthGDDTargetMul")]
        public double DepthGDDTargetMul { get; set; }

        /// <summary>Apply seed treatment</summary>
        [Description("HasSeedTreatment")]
        public bool HasSeedTreatment { get; set; }

        /// <summary>Apply soil pathogen</summary>
        [Description("HasPathogen")]
        public bool HasPathogen { get; set; }

        /// <summary>Moisture-based GDD accumulation factors (LL,DUL,SAT)</summary>
        public double[] MoistureGDDAccum { get; set; } = new double[] { 0.85, 1, 0.7 };

        /// <summary>Moisture-based GDD accumulation factors (with untreated pathogen)</summary>
        public double[] MoistureGDDAccumPathogen { get; set; } = new double[] { 0.8, 0.6, 0.4 };

        /// <summary>Moisture-based seed loss factors (LL,DUL,SAT)</summary>
        public double[] MoistureLoss { get; set; } = new double[] { 0.025, 0, 0.1 };

        /// <summary>Moisture-based seed loss factors (with untreated pathogen)</summary>
        public double[] MoistureLossPathogen { get; set; } = new double[] { 0.8, 0.6, 0.4 };

        /// <summary>Soil moisture reference</summary>
        private ISoilWater SoilRef = null;

        /// <summary>Soil physical reference</summary>
        private IPhysical PhysRef = null;

        /// <summary> Constant depth-based loss </summary>
        private double DepthSL;

        /// <summary>Fraction of phase that is complete (0-1).</summary>
        [JsonIgnore]
        public double FractionComplete
        {
            get
            {
                if (Target == 0)
                    return 1;
                else
                    return ProgressThroughPhase / Target;
            }
        }

        /// <summary>Thermal time target to end this phase.</summary>
        [JsonIgnore]
        public double Target { get; set; } 

        /// <summary>Thermal time for this time-step.</summary>
        public double TTForTimeStep { get; set; }

        /// <summary>Accumulated units of thermal time as progress through phase.</summary>
        [JsonIgnore]
        public double ProgressThroughPhase { get; set; }

        /// <summary> Moisture curve X coordinates </summary>
        private double[] xWat;

        /// <summary> Impacts of each layer </summary>
        private double[] LayerWeights;

        /// <summary>
        /// Date for emergence to occur.  null by default so model is used
        /// </summary>
        [JsonIgnore]
        public string EmergenceDate { get; set; }

        // 3. Public methods
        //-----------------------------------------------------------------------------------------------------------------

        /// <summary>Computes the phenological development during one time-step.</summary>
        /// <remarks>Returns true when target is met.</remarks>
        public bool DoTimeStep(ref double propOfDayToUse)
        {
            bool proceedToNextPhase = false;
            TTForTimeStep = phenology.thermalTime.Value() * propOfDayToUse;

            bool unused;

            double[] yWatGDD = MoistureGDDAccum;

            double SW = dot(SoilRef.SW, LayerWeights);
            double AccumMul = MathUtilities.LinearInterpReal(SW, xWat, MoistureGDDAccum, out unused);

            if (HasPathogen)
            {
                if (HasSeedTreatment)
                    AccumMul *= 0.9;
                else
                    AccumMul = MathUtilities.LinearInterpReal(SW, xWat, MoistureGDDAccumPathogen, out unused);
            }

            TTForTimeStep *= AccumMul;

            if (EmergenceDate != null)
            {
                Target = (DateUtilities.GetDate(EmergenceDate, clock.Today) - plant.SowingDate).TotalDays;
                ProgressThroughPhase += 1;
                if (DateUtilities.DatesEqual(EmergenceDate, clock.Today))
                {
                    proceedToNextPhase = true;
                }
            }
            else {
                ProgressThroughPhase += TTForTimeStep;
                if (ProgressThroughPhase > Target)
                {
                    if (TTForTimeStep > 0.0)
                    {
                        proceedToNextPhase = true;
                        propOfDayToUse = (ProgressThroughPhase - Target) / TTForTimeStep;
                        TTForTimeStep *= (1 - propOfDayToUse);
                    }
                    ProgressThroughPhase = Target;
                }
            }

            // Compute 3 seed loss curves

            // 1. Depth-based loss
            // 'DepthSL' computed at sowing time as a function of depth
            
            // 2. Moisture-based loss
            double MoistureSL;

            if (!HasPathogen || HasSeedTreatment)
                MoistureSL = MathUtilities.LinearInterpReal(SW, xWat, MoistureLoss, out unused);
            else
                MoistureSL = MathUtilities.LinearInterpReal(SW, xWat, MoistureLossPathogen, out unused);

            // 3. Temperature-based seed loss
            double TemperatureSL = TemperatureLoss.ValueForX(WeatherRef.MeanT);

            // Actual loss is the worst of the three curves.
            double ActualLoss = Math.Max(Math.Max(DepthSL, MoistureSL), TemperatureSL);

            // Apply loss
            plant.Population -= ActualLoss * plant.Population * propOfDayToUse;

            return proceedToNextPhase;
        }

        /// <summary>Resets the phase.</summary>
        public virtual void ResetPhase()
        {
            TTForTimeStep = 0;
            ProgressThroughPhase = 0;
            Target = 0;
            EmergenceDate = null;
        }

        private double dot(double[] a, double[] b)
        {
            if (a.Length != b.Length)
                    throw new Exception("Cannot dot between arrays of different length");

            double ret = 0.0;

            for (int i = 0; i < a.Length; ++i)
                ret += a[i] * b[i];

            return ret;
        }

        // 4. Private method
        //-----------------------------------------------------------------------------------------------------------------

        /// <summary>Called when [simulation commencing].</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            SoilRef = this.FindInScope<Soil>().FindChild<ISoilWater>();
            if (SoilRef == null)
                    throw new Exception("Cannot find soil");

            PhysRef = this.FindInScope<Soil>().FindChild<IPhysical>();
            if (PhysRef == null)
                    throw new Exception("Cannot find soil Physical");

            ResetPhase();
        }

        /// <summary>Called when crop is ending</summary>
        [EventSubscribe("PlantSowing")]
        private void OnPlantSowing(object sender, SowingParameters data)
        {
<<<<<<< HEAD
            Target = target.Value();
=======
            double ShootTarget = ShootLag + data.Depth * ShootRate;
            double DepthTreatTarget = DepthGDDTarget.ValueForX(data.Depth);
        
            if (HasSeedTreatment)
                DepthTreatTarget += 25;

            Target = (1 - DepthGDDTargetMul) * ShootTarget + DepthGDDTargetMul * DepthTreatTarget;

            // Find moisture X coordinates baesd on sow depth
            int layers = PhysRef.Thickness.Length;
            LayerWeights = new double[layers];
            double CurDepth = 0.0;

            for (int i = 0; i < layers; ++i)
            {
                LayerWeights[i] = Math.Min(Math.Max(0.0, data.Depth - CurDepth), PhysRef.Thickness[i]) / data.Depth;
                CurDepth += PhysRef.Thickness[i];
            }

            xWat = new double[3] {
                dot(PhysRef.LL15, LayerWeights),
                dot(PhysRef.DUL, LayerWeights),
                dot(PhysRef.SAT, LayerWeights),
            };

            DepthSL = DepthLoss.ValueForX(data.Depth);
>>>>>>> a58210d87 (implement improved emergence model)
        }

        /// <summary>Document the model.</summary>
        public override IEnumerable<ITag> Document()
        {
            // Write description of this class.
            yield return new Paragraph($"This phase goes from {Start.ToLower()} to {End.ToLower()} and simulates time to {End.ToLower()} as a function of sowing depth. The *ThermalTime Target* for ending this phase is given by:");
            yield return new Paragraph($"*Target* = *SowingDepth* x *ShootRate* + *ShootLag*");
            yield return new Paragraph($"Where:");
            yield return new Paragraph($"*SowingDepth* (mm) is sent from the manager with the sowing event.");

            // Write memos.
            foreach (var tag in DocumentChildren<Memo>())
                yield return tag;

            IFunction thermalTime = FindChild<IFunction>("ThermalTime");
            yield return new Paragraph($"Progress toward emergence is driven by thermal time accumulation{(thermalTime == null ? "" : ", where thermal time is calculated as:")}");
            if (thermalTime != null)
                foreach (var tag in thermalTime.Document())
                    yield return tag;
        }
    }
}
