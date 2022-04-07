variable_names <- c('Emerged')
observed_variable_names <- c('Emerged', 'Clock.Today')
apsimx_path <- '/home/jtst/git/ApsimX/bin/Debug/netcoreapp3.1/Models.dll'
apsimx_file <- '/tmp/CroptimizR-9c8eaf7b-71cb-422f-b08e-e9da4497c4ec/input_file.apsimx'
simulation_names <- c('ExpDepthDepth0', 'ExpDepthDepth25', 'ExpDepthDepth50', 'ExpDepthDepth75')
predicted_table_name <- 'ReportSoybean'
observed_table_name <- 'Merged'
param_info <- list(lb=c('[Soybean].Phenology.DepthGDDTarget.XYPairs.Y[1]'=60, '[Soybean].Phenology.DepthGDDTarget.XYPairs.Y[2]'=112, '[Soybean].Phenology.DepthGDDTarget.XYPairs.Y[3]'=210), ub=c('[Soybean].Phenology.DepthGDDTarget.XYPairs.Y[1]'=140, '[Soybean].Phenology.DepthGDDTarget.XYPairs.Y[2]'=204, '[Soybean].Phenology.DepthGDDTarget.XYPairs.Y[3]'=390))

optim_options=list()
optim_options$nb_rep <- 10
optim_options$xtol_rel <- 1E-05
optim_options$maxeval <- 10
optim_options$path_results <- '/tmp/CroptimizR-9c8eaf7b-71cb-422f-b08e-e9da4497c4ec'

crit_function <- CroptimizR::crit_log_cwss_corr
optim_method <- 'nloptr.simplex'

library(ApsimOnR)
library(CroptimizR)
library(dplyr)
library(nloptr)
library(DiceDesign)
library(stringr)

start_time <- Sys.time()

# Here we assume that the observed/met data is in the same directory as the .apsimx file.
files_path <- dirname(apsimx_file)

# met files path
met_files_path <- files_path

# obs path
obs_files_path <- files_path

# Runnning the model without forcing parameters
model_options=apsimx_wrapper_options(apsimx_path = apsimx_path,
                                     apsimx_file = apsimx_file,
                                     variable_names = variable_names,
                                     predicted_table_name = predicted_table_name,
                                     met_files_path = met_files_path,
                                     observed_table_name = observed_table_name,
                                     obs_files_path = obs_files_path)

sim_before_optim=apsimx_wrapper(model_options=model_options)

# observations
obs_list <- read_apsimx_output(sim_before_optim$db_file_name,
                               model_options$observed_table_name,
                               observed_variable_names,
                               names(sim_before_optim$sim_list))

simulation_names_old <- simulation_names
simulation_names <- intersect(names(obs_list), simulation_names)
obs_list=obs_list[simulation_names]

dropped_sims <- setdiff(simulation_names_old, simulation_names)
if (length(dropped_sims) > 0) {
  print(paste('NOTE: dropping simulation', dropped_sims, 'as there is no data for this simulation'))
}

# Remove "Observed." from the start of any column.
# This helps when retrieving observed data from PredictedObserved tables,
# where the observed columns all start with "Observed.", but CroptimizR
# expects the predicted and observed variables to have the same name.
for (sim_name in simulation_names) {
  for (col in names(obs_list[[sim_name]])) {
    if (startsWith(col, "Observed.")) {
      names(obs_list[[sim_name]])[names(obs_list[[sim_name]]) == col] <- str_replace(col, "Observed.", "Predicted.")
    }
  }
}

# Run the optimization
optim_output=estim_param(obs_list=obs_list,
                         crit_function=crit_function,
                         model_function=apsimx_wrapper,
                         model_options=model_options,
                         optim_options=optim_options,
                         optim_method=optim_method,
                         param_info=param_info)

duration <- as.double(difftime(Sys.time(), start_time, units = "secs"))
print(sprintf('duration: %s seconds', duration)) 

output_file <- '/tmp/CroptimizR-9c8eaf7b-71cb-422f-b08e-e9da4497c4ec/optim_results.csv'
load('/tmp/CroptimizR-9c8eaf7b-71cb-422f-b08e-e9da4497c4ec/optim_results.Rdata')
param_names <- c('DepthGDD1', 'DepthGDD2', 'DepthGDD3')

objectives <- c()
for (rep in res$nlo) {
  objectives <- c(objectives, rep$objective)
}
optimal_index <- which.min(objectives)

df <- NULL
for (i in 1:length(res$nlo)) {
  row <- res$nlo[[i]]
  initial <- row$x0
  solution <- row$solution
  msg <- row$message
  objective <- row$objective
  iterations <- row$iterations
  
  vals <- c()
  for (j in 1:length(param_names)) {
    vals <- c(vals, initial[j], solution[j])
  }
  rowdata <- c(i, i == optimal_index, objective, iterations, vals, msg)
  df <- rbind(df, rowdata)
}

cols <- c('Repetition', 'Is Optimal', 'Objective Function Value', 'Number of Iterations')
for (param in param_names) {
  cols <- c(cols, paste(param, 'Initial'))
  cols <- c(cols, paste(param, 'Final'))
}
cols <- c(cols, 'Message')
colnames(df) <- cols
write.table(df, file = output_file, row.names = F, col.names = T, sep = ',')


