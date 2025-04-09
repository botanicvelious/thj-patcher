using System.Collections.Generic;

namespace THJPatcher.Models
{
    /// <summary>
    /// Provides loading messages displayed to the user during application startup
    /// </summary>
    public class LoadingMessages
    {
        public List<string> Messages { get; set; }

        public static LoadingMessages CreateDefault()
        {
            return new LoadingMessages
            {
                Messages = new List<string>
                {
                    "Decreasing exp by 1.1%, how would you know?...",
                    "Decreasing enemy health to 0 while keeping yours above 0...",
                    "Cata is messing with the wires again....",
                    "Feeding halflings to ogres...",
                    "Casting lightning bolt...",
                    "I didn't ask how big the room was… I said I cast Fireball...",
                    "Increasing Froglok jumping by 3.8%...",
                    "Banning MQ2 users...",
                    "Nerfing Monk/Monk/Monk...",
                    "Reverting the bug fixes by pulling from upstream...",
                    "Feeding the server hamsters...",
                    "Bribing the RNG gods...",
                    "Aporia is watching you.....",
                    "Standby.....building a gazebo.....",
                    "Gnomes are not snacks, stop asking...",
                    "Buffing in the Plane of Water… just kidding, no one goes there...",
                    "Undercutting your trader by 1 copper...",
                    "Emergency patch: Aporia found an exploit. Cata is 'fixing' it. Drake is taking cover...",
                    "'Balancing' triple-class builds...",
                    "Adding more duct tape to the database—should be fine...",
                    "Server hamster demands a raise, Aporia Refused...",
                    "'Balancing' pet builds...",
                    "I Pity the Fool...",
                    "You will not evade me.....",
                    "You have ruined your own lands..... you will not ruin mine!....",
                    "Im winning at Fashion Quest...",
                    "Welcome to The Heroes Journey—where your class build is only limited by your imagination.",
                    "Three-class builds allow for endless customization—choose wisely, adventure boldly!",
                    "The Tribunal have ruled: Your Rogue/Warrior/Monk build is technically a crime.",
                    "Plane of Fire..... the BRD/WIZ/SK's new home.....",
                    "Zebuxoruk is confused by your triple-class build. Not as confused as we are, though...",
                    "Even the Gods are questioning your choice of PAL/SK/MNK. But hey, it's your journey...",
                    "Bristlebane is rewriting the loot tables… good luck!...",
                    "Entering Vex Thal… see you next year...",
                    "Heading into Plane of Time… don't worry, you will need plenty of it....",
                    "Aporia tried to nerf Bards… but they twisted out of it.",
                    "Xegony's winds are howling… or is that just the lag?...."
                }
            };
        }
    }
} 