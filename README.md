Machine Learning on higher dimensional time series data.

The following preparatory steps are necessary to construct our model:
1.  Collect time series data from your data provider of choice, I chose Oanda as my provider for foreign exchange information.
2.  Store time series data in csv files for easy reference.  I collected data at 2 minute intervals for one month periods, between January 2005 and December 2018.
3.  Compute statistical analysis metrics such as average, standard deviation and correlation for the time series.
	a. The time intervals in steps 3 and 4 are subject to hyperparameter tuning.

The following folder structure is utilized for storing informtion:
- C:\JonesOnie\CSV\MinuteData
- C:\JonesOnie\CSV\Statistics\Averages\Backward
- C:\JonesOnie\CSV\Statistics\Averages\Forward
- C:\JonesOnie\CSV\Statistics\Deviations\Backward
- C:\JonesOnie\CSV\Statistics\Deviations\Forward
- C:\JonesOnie\CSV\Statistics\Correlations\Instruments\Backward
- C:\JonesOnie\CSV\Statistics\Correlations\Instruments\Forward
- C:\JonesOnie\CSV\Optimizations
- C:\JonesOnie\CSV\Projections
	
